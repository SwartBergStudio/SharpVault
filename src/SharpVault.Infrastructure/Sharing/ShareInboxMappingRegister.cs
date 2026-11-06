// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Sharing;

public sealed class ShareInboxMappingRegister : IRegister
{
    private const string OwnerFormat = "D";

    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<ShareInboxEntry, ShareInboxTableEntity>().MapWith(s => ToEntity(s));
        config.NewConfig<ShareInboxTableEntity, ShareInboxEntry>().MapWith(e => ToDomain(e));
    }

    internal static ShareInboxTableEntity ToEntity(ShareInboxEntry entry) => new()
    {
        PartitionKey = ShareInboxKeys.PartitionKey(entry.RecipientId),
        RowKey = ShareInboxKeys.RowKey(entry.OwnerId, entry.ItemKind, entry.ItemId),
        OwnerId = entry.OwnerId.ToString(OwnerFormat),
        ItemKind = entry.ItemKind.ToString(),
        ItemId = entry.ItemId.ToString(OwnerFormat),
        SharedAt = entry.SharedAt,
        FiledFolderKey = entry.FiledFolderId is { } folderId ? VaultKeys.FolderKey(folderId) : null,
    };

    internal static ShareInboxEntry ToDomain(ShareInboxTableEntity entity) => new()
    {
        RecipientId = Guid.ParseExact(entity.PartitionKey, OwnerFormat),
        OwnerId = Guid.ParseExact(entity.OwnerId, OwnerFormat),
        ItemKind = VaultStorageMappingRegister.ParseItemKind(entity.ItemKind),
        ItemId = Guid.ParseExact(entity.ItemId, OwnerFormat),
        SharedAt = entity.SharedAt,
        FiledFolderId = VaultKeys.ParseFolderKey(entity.FiledFolderKey),
    };
}
