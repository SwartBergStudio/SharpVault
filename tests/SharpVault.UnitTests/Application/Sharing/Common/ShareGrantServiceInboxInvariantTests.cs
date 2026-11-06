// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Common;

[Trait("Category", "Security")]
public sealed class ShareGrantServiceInboxInvariantTests
{
    private readonly InMemoryInbox _inbox = new();
    private readonly IVaultStore _store = Substitute.For<IVaultStore>();

    [Fact]
    public async Task AddAsync_InboxRowRemovedByRacingRevokeBeforeCommit_RestoresTheInboxRow()
    {
        var grant = NewGrant();
        _store.SubmitAsync(grant.OwnerId, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _inbox.Rows.Clear();
                return Task.FromResult(new VaultWriteResult(StoreWriteResult.Written, 1, ["etag"]));
            });

        await NewService().AddAsync(grant, null, TestContext.Current.CancellationToken);

        Assert.Single(_inbox.Rows);
    }

    [Fact]
    public async Task AddAsync_GrantAlreadyExistsAndInboxRowMissing_RestoresTheInboxRow()
    {
        var grant = NewGrant();
        _store.SubmitAsync(grant.OwnerId, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _inbox.Rows.Clear();
                return Task.FromResult(new VaultWriteResult(StoreWriteResult.AlreadyExists, 0, []));
            });

        var write = await NewService().AddAsync(grant, null, TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.AlreadyExists, write.Result);
        Assert.Single(_inbox.Rows);
    }

    [Fact]
    public async Task AddAsync_NoRace_WritesInboxOnce()
    {
        var grant = NewGrant();
        _store.SubmitAsync(grant.OwnerId, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VaultWriteResult(StoreWriteResult.Written, 1, ["etag"])));

        await NewService().AddAsync(grant, null, TestContext.Current.CancellationToken);

        Assert.Single(_inbox.Rows);
        Assert.Equal(1, _inbox.AddCalls);
    }

    [Fact]
    public async Task RevokeAsync_GrantAppearsWhileRemovingInboxRow_RestoresTheInboxRow()
    {
        var grant = NewGrant();
        await _inbox.AddIfAbsentAsync(
            new ShareInboxEntry { RecipientId = grant.RecipientId, OwnerId = grant.OwnerId, ItemKind = grant.ItemKind, ItemId = grant.ItemId },
            TestContext.Current.CancellationToken);
        var reads = 0;
        _store.GetShareGrantRowsAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(reads++ == 0 ? null : new ShareGrantRows(new Versioned<ShareGrant>(grant, "gi"), "gr")));

        var outcome = await NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, TestContext.Current.CancellationToken);

        Assert.Equal(ShareRevokeResult.NotFound, outcome.Result);
        Assert.Single(_inbox.Rows);
    }

    private static ShareGrant NewGrant() => new()
    {
        OwnerId = Guid.NewGuid(),
        ItemKind = ShareItemKind.Secret,
        ItemId = Guid.NewGuid(),
        RecipientId = Guid.NewGuid(),
        Permission = SharePermission.View,
    };

    private ShareGrantService NewService() => new(_store, _inbox, TimeProvider.System, NullLogger<ShareGrantService>.Instance);

    private sealed class InMemoryInbox : IShareInboxStore
    {
        public List<ShareInboxEntry> Rows { get; } = [];

        public int AddCalls { get; private set; }

        public Task AddIfAbsentAsync(ShareInboxEntry entry, CancellationToken cancellationToken)
        {
            if (!Rows.Any(row => Matches(row, entry.RecipientId, entry.OwnerId, entry.ItemKind, entry.ItemId)))
            {
                Rows.Add(entry);
                AddCalls++;
            }

            return Task.CompletedTask;
        }

        public Task<Versioned<ShareInboxEntry>?> GetAsync(Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken)
        {
            var row = Rows.FirstOrDefault(candidate => Matches(candidate, recipientId, ownerId, kind, itemId));
            return Task.FromResult(row is null ? null : new Versioned<ShareInboxEntry>(row, "etag"));
        }

        public Task<StoreWriteResult> SetFiledFolderAsync(ShareInboxEntry entry, string etag, CancellationToken cancellationToken) =>
            Task.FromResult(StoreWriteResult.Written);

        public Task<bool> RemoveAsync(Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.RemoveAll(row => Matches(row, recipientId, ownerId, kind, itemId)) > 0);

        public Task<ShareInboxPage> ListAsync(Guid recipientId, Guid? filedFolderId, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
            Task.FromResult(new ShareInboxPage([.. Rows.Where(row => row.RecipientId == recipientId).Select(row => new Versioned<ShareInboxEntry>(row, "etag"))], null));

        private static bool Matches(ShareInboxEntry row, Guid recipientId, Guid ownerId, ShareItemKind kind, Guid itemId) =>
            row.RecipientId == recipientId && row.OwnerId == ownerId && row.ItemKind == kind && row.ItemId == itemId;
    }
}
