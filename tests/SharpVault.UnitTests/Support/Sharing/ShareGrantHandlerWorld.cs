// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Users;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Support.Sharing;

internal sealed class ShareGrantHandlerWorld : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly object _gate = new();
    private readonly List<ShareGrant> _grants = [];
    private readonly Dictionary<Guid, string> _etags = [];
    private readonly ILoggerFactory _loggerFactory;
    private int _etagCounter;

    public ShareGrantHandlerWorld(bool callerIsOwner)
    {
        Caller = new StubCurrentUser();
        Chain = callerIsOwner ? new ShareChainWorld(Caller.UserId) : new ShareChainWorld();
        Chain.CallerId = Caller.UserId;
        Time = new FakeTimeProvider(Now);
        Logs = new CapturingLoggerProvider();
        _loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        Directory = Substitute.For<IUserDirectory>();
        Users = Substitute.For<IUserStore>();
        Inbox = Substitute.For<IShareInboxStore>();
        AuditLog = new RecordingAuditLog();
        Audit = new VaultAuditWriter(Caller, new StubRequestMetadata(), AuditLog, Time, _loggerFactory.CreateLogger<VaultAuditWriter>());
        Resolver = new ShareAccessResolver(Chain.Store, Caller);
        Gate = new ShareManagementGate(Chain.Store, Resolver, Caller);
        GrantService = new ShareGrantService(Chain.Store, Inbox, Time, _loggerFactory.CreateLogger<ShareGrantService>());
        ConfigureStore();
    }

    public StubCurrentUser Caller { get; }

    public ShareChainWorld Chain { get; }

    public Guid OwnerId => Chain.OwnerId;

    public FakeTimeProvider Time { get; }

    public CapturingLoggerProvider Logs { get; }

    public IUserDirectory Directory { get; }

    public IUserStore Users { get; }

    public IShareInboxStore Inbox { get; }

    public RecordingAuditLog AuditLog { get; }

    public VaultAuditWriter Audit { get; }

    public ShareAccessResolver Resolver { get; }

    public ShareManagementGate Gate { get; }

    public ShareGrantService GrantService { get; }

    public List<IReadOnlyList<VaultChange>> Submitted { get; } = [];

    public VaultWriteResult? NextWrite { get; set; }

    public Func<Task>? BeforeSubmit { get; set; }

    public IReadOnlyList<ShareGrant> Grants
    {
        get
        {
            lock (_gate)
            {
                return [.. _grants];
            }
        }
    }

    public ILogger<T> LoggerFor<T>() => _loggerFactory.CreateLogger<T>();

    public string ETagOf(Guid recipientId)
    {
        lock (_gate)
        {
            return _etags[recipientId];
        }
    }

    public ShareGrant AddGrant(
        Guid recipientId,
        Guid grantedById,
        SharePermission permission,
        ShareItemKind kind,
        Guid itemId,
        string? displayName = null)
    {
        var grant = new ShareGrant
        {
            OwnerId = OwnerId,
            ItemKind = kind,
            ItemId = itemId,
            RecipientId = recipientId,
            Permission = permission,
            RecipientDisplayName = displayName ?? "Recipient " + recipientId.ToString("N")[..8],
            RecipientEmail = "recipient@example.test",
            GrantedById = grantedById,
            GrantedByDisplayName = "Granting User",
            GrantedAt = Now.AddDays(-1),
            ModifiedAt = Now.AddDays(-1),
        };
        lock (_gate)
        {
            _grants.Add(grant);
            _etags[recipientId] = NextETag();
        }

        return grant;
    }

    public void ArrangeItem(ShareItemKind kind, Guid itemId)
    {
        if (kind == ShareItemKind.Secret)
        {
            Chain.ActiveSecret(itemId, null);
        }
        else
        {
            Chain.ActiveFolder(itemId, null);
        }
    }

    public void ArrangeRecycledItem(ShareItemKind kind, Guid itemId)
    {
        if (kind == ShareItemKind.Secret)
        {
            Chain.RecycledSecret(itemId);
        }
        else
        {
            Chain.RecycledFolder(itemId);
        }
    }

    public void MakeCallerRecipient(ShareItemKind kind, Guid itemId, SharePermission permission)
    {
        ArrangeItem(kind, itemId);
        if (kind == ShareItemKind.Secret)
        {
            AddGrant(Caller.UserId, OwnerId, permission, kind, itemId);
        }
        else
        {
            Chain.FolderGrant(itemId, permission);
        }
    }

    public void MakeCallerManager(ShareItemKind kind, Guid itemId) => MakeCallerRecipient(kind, itemId, SharePermission.Manage);

    public void KnownMember(Guid id, string displayName = "Known Person", string? email = "known@example.test") =>
        Directory.FindMemberAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DirectoryUser?>(new DirectoryUser(id, displayName, email)));

    public void UserRow(Guid id, UserStatus status) =>
        Users.GetAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<User>?>(new Versioned<User>(new User { Id = id, DisplayName = "Row", Status = status }, "u")));

    public void Dispose() => _loggerFactory.Dispose();

    private string NextETag() =>
        string.Create(CultureInfo.InvariantCulture, $"W/\"datetime'2026-03-01T09%3A00%3A{Interlocked.Increment(ref _etagCounter):D2}.0000000Z'\"");

    private void ConfigureStore()
    {
        var store = Chain.Store;
        store.GetShareGrantAsync(OwnerId, Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Find(call.ArgAt<ShareItemKind>(1), call.ArgAt<Guid>(2), call.ArgAt<Guid>(3)) is { } grant
                ? new Versioned<ShareGrant>(grant, ETagOf(grant.RecipientId))
                : null));
        store.GetShareGrantRowsAsync(OwnerId, Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Find(call.ArgAt<ShareItemKind>(1), call.ArgAt<Guid>(2), call.ArgAt<Guid>(3)) is { } grant
                ? new ShareGrantRows(new Versioned<ShareGrant>(grant, ETagOf(grant.RecipientId)), "recipient-row-etag")
                : null));
        store.ListShareGrantsForItemAsync(OwnerId, Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var kind = call.ArgAt<ShareItemKind>(1);
                var itemId = call.ArgAt<Guid>(2);
                var limit = call.ArgAt<int>(3);
                lock (_gate)
                {
                    IReadOnlyList<Versioned<ShareGrant>> rows =
                    [
                        .. _grants
                            .Where(grant => grant.ItemKind == kind && grant.ItemId == itemId)
                            .OrderBy(grant => grant.RecipientId)
                            .Take(limit)
                            .Select(grant => new Versioned<ShareGrant>(grant, _etags[grant.RecipientId])),
                    ];
                    return Task.FromResult(rows);
                }
            });
        store.SubmitAsync(OwnerId, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var changes = call.Arg<IReadOnlyList<VaultChange>>();
                if (BeforeSubmit is { } before)
                {
                    await before();
                }

                lock (_gate)
                {
                    Submitted.Add(changes);
                    return NextWrite ?? Apply(changes);
                }
            });
    }

    private ShareGrant? Find(ShareItemKind kind, Guid itemId, Guid recipientId)
    {
        lock (_gate)
        {
            return _grants.FirstOrDefault(grant => grant.ItemKind == kind && grant.ItemId == itemId && grant.RecipientId == recipientId);
        }
    }

    private VaultWriteResult Apply(IReadOnlyList<VaultChange> changes)
    {
        var etags = new List<string?>();
        foreach (var change in changes)
        {
            var grant = change.Grant!;
            switch (change.Kind)
            {
                case VaultChangeKind.AddShareGrant:
                    if (_grants.Any(existing => existing.RecipientId == grant.RecipientId && existing.ItemId == grant.ItemId && existing.ItemKind == grant.ItemKind))
                    {
                        return new VaultWriteResult(StoreWriteResult.AlreadyExists, etags.Count, etags);
                    }

                    _grants.Add(grant);
                    _etags[grant.RecipientId] = NextETag();
                    etags.Add(_etags[grant.RecipientId]);
                    break;
                case VaultChangeKind.ReplaceShareGrant:
                    _etags[grant.RecipientId] = NextETag();
                    etags.Add(_etags[grant.RecipientId]);
                    break;
                case VaultChangeKind.DeleteShareGrant:
                    _grants.RemoveAll(existing => existing.RecipientId == grant.RecipientId && existing.ItemId == grant.ItemId);
                    etags.Add(null);
                    break;
                default:
                    throw new InvalidOperationException("Unexpected change " + change.Kind);
            }
        }

        return new VaultWriteResult(StoreWriteResult.Written, changes.Count, etags);
    }
}
