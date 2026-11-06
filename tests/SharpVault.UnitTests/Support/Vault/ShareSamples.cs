// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Sharing;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.UnitTests.Support.Vault;

internal static class ShareSamples
{
    public static ShareGrant Grant(
        Guid owner,
        ShareItemKind kind = ShareItemKind.Secret,
        Guid? itemId = null,
        Guid? recipient = null,
        SharePermission permission = SharePermission.View) => new()
    {
        OwnerId = owner,
        ItemKind = kind,
        ItemId = itemId ?? Guid.NewGuid(),
        RecipientId = recipient ?? Guid.NewGuid(),
        Permission = permission,
        RecipientDisplayName = "Share Recipient",
        RecipientEmail = "recipient@example.test",
        GrantedById = owner,
        GrantedByDisplayName = "Share Owner",
        GrantedAt = VaultSamples.Created,
        ModifiedAt = VaultSamples.Created,
    };

    public static ShareInboxEntry InboxEntry(Guid recipient, Guid? owner = null, ShareItemKind kind = ShareItemKind.Secret, Guid? itemId = null, Guid? filedFolderId = null) => new()
    {
        RecipientId = recipient,
        OwnerId = owner ?? Guid.NewGuid(),
        ItemKind = kind,
        ItemId = itemId ?? Guid.NewGuid(),
        SharedAt = VaultSamples.Created,
        FiledFolderId = filedFolderId,
    };

    public static ShareGrantTableEntity GrantRow(ShareGrant grant, bool byItem = true) => VaultStorageMappingRegister.ToEntity(grant, byItem);

    public static ShareGrantTableEntity DamagedGrantRow(Guid owner)
    {
        var row = GrantRow(Grant(owner));
        row.ItemKind = "Bogus";
        return row;
    }

    public static ShareInboxTableEntity InboxRow(ShareInboxEntry entry) => ShareInboxMappingRegister.ToEntity(entry);

    public static ShareInboxTableEntity DamagedInboxRow(Guid recipient)
    {
        var row = InboxRow(InboxEntry(recipient));
        row.ItemKind = "Bogus";
        return row;
    }
}
