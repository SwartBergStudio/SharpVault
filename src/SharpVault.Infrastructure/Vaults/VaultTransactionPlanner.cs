// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;

namespace SharpVault.Infrastructure.Vaults;

internal static class VaultTransactionPlanner
{
    private const string OwnerMismatchMessage = "A vault change belongs to a different owner than the write.";
    private const string KeyRingTouchPlacementMessage = "TouchKeyRing may appear only once, at index 0.";
    private const string MissingETagMessage = "The vault change has no ETag.";

    public static IReadOnlyList<PlannedTransaction> Plan(Guid ownerId, IReadOnlyList<VaultChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            return [];
        }

        var leadsWithKeyRingTouch = ValidateKeyRingTouch(changes);
        var partitionKey = VaultKeys.PartitionKey(ownerId);
        var chunks = new List<PlannedTransaction>();
        var chunk = new ChunkBuilder(leadsWithKeyRingTouch: false, partitionKey, null);

        for (var index = 0; index < changes.Count; index++)
        {
            var planned = PlanChange(ownerId, partitionKey, changes[index]);
            if (!chunk.Fits(planned))
            {
                chunks.Add(chunk.Build());
                chunk = new ChunkBuilder(leadsWithKeyRingTouch, partitionKey, changes[0].ETag);
            }

            chunk.Add(index, planned);
        }

        chunks.Add(chunk.Build());
        return chunks;
    }

    public static TableTransactionAction KeyRingTouch(string partitionKey, string etag)
    {
        var entity = new TableEntity(partitionKey, VaultKeys.KeyRingRowKey);
        return new TableTransactionAction(TableTransactionActionType.UpdateMerge, entity, new ETag(etag));
    }

    private static bool ValidateKeyRingTouch(IReadOnlyList<VaultChange> changes)
    {
        for (var index = 1; index < changes.Count; index++)
        {
            if (changes[index].Kind == VaultChangeKind.TouchKeyRing)
            {
                throw new ArgumentException(KeyRingTouchPlacementMessage, nameof(changes));
            }
        }

        return changes[0].Kind == VaultChangeKind.TouchKeyRing;
    }

    private static PlannedChange PlanChange(Guid ownerId, string partitionKey, VaultChange change) => change.Kind switch
    {
        VaultChangeKind.AddSecret => Single(Add(SecretEntity(ownerId, change)), primary: 0),
        VaultChangeKind.ReplaceSecret => Single(Replace(SecretEntity(ownerId, change), change), primary: 0),
        VaultChangeKind.RecycleSecret => Pair(DeleteSecret(partitionKey, change, recycled: false), Add(SecretEntity(ownerId, change))),
        VaultChangeKind.RestoreSecret => Pair(DeleteSecret(partitionKey, change, recycled: true), Add(SecretEntity(ownerId, change))),
        VaultChangeKind.PurgeSecret => Single(DeleteSecret(partitionKey, change, recycled: true), primary: null),
        VaultChangeKind.AddVersion => Single(Add(VersionEntity(ownerId, change)), primary: 0),
        VaultChangeKind.DeleteVersion => Single(DeleteVersion(partitionKey, change), primary: null),
        VaultChangeKind.AddFolder => Single(Add(FolderEntity(ownerId, change)), primary: 0),
        VaultChangeKind.ReplaceFolder => Single(Replace(FolderEntity(ownerId, change), change), primary: 0),
        VaultChangeKind.RecycleFolder => Pair(DeleteFolder(partitionKey, change, recycled: false), Add(FolderEntity(ownerId, change))),
        VaultChangeKind.RestoreFolder => Pair(DeleteFolder(partitionKey, change, recycled: true), Add(FolderEntity(ownerId, change))),
        VaultChangeKind.PurgeFolder => Single(DeleteFolder(partitionKey, change, recycled: true), primary: null),
        VaultChangeKind.RemoveSecret => Single(DeleteSecret(partitionKey, change, recycled: false), primary: null),
        VaultChangeKind.RemoveRecycledSecret => Single(DeleteSecret(partitionKey, change, recycled: true), primary: null),
        VaultChangeKind.RemoveFolder => Single(DeleteFolder(partitionKey, change, recycled: false), primary: null),
        VaultChangeKind.RemoveRecycledFolder => Single(DeleteFolder(partitionKey, change, recycled: true), primary: null),
        VaultChangeKind.TouchFolder => Single(Touch(partitionKey, VaultKeys.FolderRowKey(change.ItemId, recycled: false), change), primary: 0),
        VaultChangeKind.TouchKeyRing => Single(Touch(partitionKey, VaultKeys.KeyRingRowKey, change), primary: 0),
        VaultChangeKind.AddShareGrant => GrantPair(Add(GrantEntity(ownerId, change, byItem: true)), Add(GrantEntity(ownerId, change, byItem: false))),
        VaultChangeKind.ReplaceShareGrant => GrantPair(ReplaceGrant(GrantEntity(ownerId, change, byItem: true), RequireETag(change)), ReplaceGrant(GrantEntity(ownerId, change, byItem: false), RequireSecondaryETag(change))),
        VaultChangeKind.DeleteShareGrant => DeleteGrantPair(ownerId, partitionKey, change),
        VaultChangeKind.DeleteShareGrantByItem => Single(Delete(partitionKey, ByItemRowKeyOf(ownerId, change), change), primary: null),
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };

    private static PlannedChange Single(PlannedAction action, int? primary) => new([action], primary);

    private static PlannedChange GrantPair(PlannedAction byItem, PlannedAction byRecipient) => new([byItem, byRecipient], 0);

    private static string ByItemRowKeyOf(Guid ownerId, VaultChange change)
    {
        var grant = GrantOf(ownerId, change);
        return VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId);
    }

    private static PlannedChange DeleteGrantPair(Guid ownerId, string partitionKey, VaultChange change)
    {
        var grant = GrantOf(ownerId, change);
        var byItem = Delete(partitionKey, VaultKeys.GrantByItemRowKey(grant.ItemKind, grant.ItemId, grant.RecipientId), change);
        var byRecipient = new PlannedAction(
            new TableTransactionAction(
                TableTransactionActionType.Delete,
                new TableEntity(partitionKey, VaultKeys.GrantByRecipientRowKey(grant.RecipientId, grant.ItemKind, grant.ItemId)),
                new ETag(RequireSecondaryETag(change))),
            0);
        return new PlannedChange([byItem, byRecipient], null);
    }

    private static PlannedAction ReplaceGrant(ITableEntity entity, string etag) =>
        new(new TableTransactionAction(TableTransactionActionType.UpdateReplace, entity, new ETag(etag)), 0);

    private static PlannedChange Pair(PlannedAction delete, PlannedAction add) => new([delete, add], 1);

    private static PlannedAction Add(ITableEntity entity) =>
        new(new TableTransactionAction(TableTransactionActionType.Add, entity), PayloadOf(entity));

    private static PlannedAction Replace(ITableEntity entity, VaultChange change) =>
        new(new TableTransactionAction(TableTransactionActionType.UpdateReplace, entity, new ETag(RequireETag(change))), PayloadOf(entity));

    private static PlannedAction Touch(string partitionKey, string rowKey, VaultChange change) =>
        new(new TableTransactionAction(TableTransactionActionType.UpdateMerge, new TableEntity(partitionKey, rowKey), new ETag(RequireETag(change))), 0);

    private static PlannedAction DeleteSecret(string partitionKey, VaultChange change, bool recycled) =>
        Delete(partitionKey, VaultKeys.SecretRowKey(change.ItemId, recycled), change);

    private static PlannedAction DeleteFolder(string partitionKey, VaultChange change, bool recycled) =>
        Delete(partitionKey, VaultKeys.FolderRowKey(change.ItemId, recycled), change);

    private static PlannedAction DeleteVersion(string partitionKey, VaultChange change)
    {
        var version = change.Version!;
        return Delete(partitionKey, VaultKeys.VersionRowKey(version.SecretId, version.Id), change);
    }

    private static PlannedAction Delete(string partitionKey, string rowKey, VaultChange change) =>
        new(new TableTransactionAction(TableTransactionActionType.Delete, new TableEntity(partitionKey, rowKey), new ETag(RequireETag(change))), 0);

    private static SecretTableEntity SecretEntity(Guid ownerId, VaultChange change)
    {
        var secret = change.Secret!;
        RequireOwner(ownerId, secret.OwnerId);
        return VaultStorageMappingRegister.ToEntity(secret);
    }

    private static FolderTableEntity FolderEntity(Guid ownerId, VaultChange change)
    {
        var folder = change.Folder!;
        RequireOwner(ownerId, folder.OwnerId);
        return VaultStorageMappingRegister.ToEntity(folder);
    }

    private static SecretVersionTableEntity VersionEntity(Guid ownerId, VaultChange change)
    {
        var version = change.Version!;
        RequireOwner(ownerId, version.OwnerId);
        return VaultStorageMappingRegister.ToEntity(version);
    }

    private static ShareGrant GrantOf(Guid ownerId, VaultChange change)
    {
        var grant = change.Grant!;
        RequireOwner(ownerId, grant.OwnerId);
        return grant;
    }

    private static ShareGrantTableEntity GrantEntity(Guid ownerId, VaultChange change, bool byItem) =>
        VaultStorageMappingRegister.ToEntity(GrantOf(ownerId, change), byItem);

    private static string RequireSecondaryETag(VaultChange change) =>
        string.IsNullOrWhiteSpace(change.SecondaryETag) ? throw new ArgumentException(MissingETagMessage) : change.SecondaryETag;

    private static void RequireOwner(Guid expected, Guid actual)
    {
        if (expected != actual)
        {
            throw new ArgumentException(OwnerMismatchMessage);
        }
    }

    private static string RequireETag(VaultChange change) =>
        string.IsNullOrWhiteSpace(change.ETag) ? throw new ArgumentException(MissingETagMessage) : change.ETag;

    private static int PayloadOf(ITableEntity entity) =>
        entity is IBinaryPayload payload ? (int)(((long)payload.BinaryLength * 4 + 2) / 3) : 0;

    private sealed record PlannedAction(TableTransactionAction Action, int PayloadBytes);

    private sealed record PlannedChange(IReadOnlyList<PlannedAction> Actions, int? PrimaryOffset)
    {
        public int Bytes => Actions.Sum(action => VaultStorageLimits.ActionOverheadBytes + action.PayloadBytes);
    }

    private sealed class ChunkBuilder
    {
        private readonly List<TableTransactionAction> _actions = [];
        private readonly List<int> _changeIndexes = [];
        private readonly List<int?> _primaryIndexes = [];
        private readonly bool _leadsWithTouch;
        private int _bytes;

        public ChunkBuilder(bool leadsWithKeyRingTouch, string partitionKey, string? keyRingETag)
        {
            _leadsWithTouch = leadsWithKeyRingTouch;
            if (leadsWithKeyRingTouch)
            {
                _actions.Add(KeyRingTouch(partitionKey, keyRingETag!));
                _bytes = VaultStorageLimits.ActionOverheadBytes;
            }
        }

        public bool Fits(PlannedChange change)
        {
            if (_changeIndexes.Count == 0)
            {
                return true;
            }

            return _actions.Count + change.Actions.Count <= VaultStorageLimits.MaxActionsPerTransaction
                && _bytes + change.Bytes <= VaultStorageLimits.MaxTransactionPayloadBytes;
        }

        public void Add(int changeIndex, PlannedChange change)
        {
            var first = _actions.Count;
            _actions.AddRange(change.Actions.Select(action => action.Action));
            _changeIndexes.Add(changeIndex);
            _primaryIndexes.Add(change.PrimaryOffset is { } offset ? first + offset : null);
            _bytes += change.Bytes;
        }

        public PlannedTransaction Build() => new(_actions, _changeIndexes, _primaryIndexes, _leadsWithTouch);
    }
}
