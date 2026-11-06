// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Runtime.ExceptionServices;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class GrantCascadeWorld
{
    public const int ChunkSize = 100;

    private readonly object _gate = new();
    private readonly List<ShareGrant> _grants = [];
    private readonly HashSet<(Guid Recipient, ShareItemKind Kind, Guid Item)> _inbox = [];
    private readonly HashSet<Guid> _erasedItems = [];
    private int _chunksSubmitted;

    public int FailChunkNumber { get; set; }

    public Exception? FailWith { get; set; }

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

    public int InboxRows
    {
        get
        {
            lock (_gate)
            {
                return _inbox.Count;
            }
        }
    }

    public bool IsErased(Guid itemId)
    {
        lock (_gate)
        {
            return _erasedItems.Contains(itemId);
        }
    }

    public void AddGrants(Guid owner, ShareItemKind kind, Guid itemId, int count)
    {
        lock (_gate)
        {
            for (var index = 0; index < count; index++)
            {
                var grant = NewGrant(owner, kind, itemId);
                _grants.Add(grant);
                _inbox.Add((grant.RecipientId, kind, itemId));
            }
        }
    }

    public void ResetFailure()
    {
        lock (_gate)
        {
            FailChunkNumber = 0;
            FailWith = null;
            _chunksSubmitted = 0;
        }
    }

    public void Wire(IVaultStore store, IShareInboxStore inbox)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(inbox);

        store.ListShareGrantsForItemAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<Versioned<ShareGrant>>>(
                [.. Grants.Where(grant => grant.ItemKind == call.ArgAt<ShareItemKind>(1) && grant.ItemId == call.ArgAt<Guid>(2)).Select(grant => new Versioned<ShareGrant>(grant, "item"))]));
        store.GetShareGrantByRecipientETagAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var found = Grants.Any(grant =>
                    grant.ItemKind == call.ArgAt<ShareItemKind>(1) && grant.ItemId == call.ArgAt<Guid>(2) && grant.RecipientId == call.ArgAt<Guid>(3));
                return Task.FromResult(found ? "recipient" : null);
            });
        store.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Submit(call.Arg<IReadOnlyList<VaultChange>>())));
        inbox.RemoveAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (_gate)
                {
                    return Task.FromResult(_inbox.Remove((call.ArgAt<Guid>(0), call.ArgAt<ShareItemKind>(2), call.ArgAt<Guid>(3))));
                }
            });
    }

    public VaultWriteResult Submit(IReadOnlyList<VaultChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        lock (_gate)
        {
            var completed = 0;
            foreach (var chunk in changes.Chunk(ChunkSize))
            {
                _chunksSubmitted++;
                if (_chunksSubmitted == FailChunkNumber)
                {
                    var etags = new string?[changes.Count];
                    return FailWith is { } failure
                        ? new VaultWriteResult(StoreWriteResult.Conflict, completed, etags, ExceptionDispatchInfo.Capture(failure))
                        : new VaultWriteResult(StoreWriteResult.Conflict, completed, etags);
                }

                foreach (var change in chunk)
                {
                    Apply(change);
                }

                completed += chunk.Length;
            }

            return new VaultWriteResult(StoreWriteResult.Written, completed, [.. changes.Select(_ => (string?)"w")]);
        }
    }

    private static ShareGrant NewGrant(Guid owner, ShareItemKind kind, Guid itemId) => new()
    {
        OwnerId = owner,
        ItemKind = kind,
        ItemId = itemId,
        RecipientId = Guid.NewGuid(),
        Permission = SharePermission.View,
        RecipientDisplayName = "Share Recipient",
        RecipientEmail = "recipient@example.test",
        GrantedById = owner,
        GrantedByDisplayName = "Share Owner",
        GrantedAt = VaultSamples.Created,
        ModifiedAt = VaultSamples.Created,
    };

    private void Apply(VaultChange change)
    {
        switch (change.Kind)
        {
            case VaultChangeKind.DeleteShareGrant:
                _grants.RemoveAll(grant => grant.ItemKind == change.Grant!.ItemKind && grant.ItemId == change.Grant.ItemId && grant.RecipientId == change.Grant.RecipientId);
                break;
            case VaultChangeKind.PurgeSecret:
                _erasedItems.Add(change.Secret!.Id);
                break;
            case VaultChangeKind.PurgeFolder:
                _erasedItems.Add(change.Folder!.Id);
                break;
        }
    }
}
