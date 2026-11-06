// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public static class ShareAuditItems
{
    public static VaultAuditItem Shared(ShareGrant grant) => Build(grant, SharedEvent(grant));

    public static VaultAuditItem Changed(ShareGrant grant) => Build(grant, ChangedEvent(grant));

    public static VaultAuditItem Revoked(ShareGrant grant) => Build(grant, RevokedEvent(grant));

    public static VaultAuditItem Erased(ShareGrant grant) => Build(grant, AuditEventType.ShareErased);

    private static VaultAuditItem Build(ShareGrant grant, AuditEventType eventType)
    {
        ArgumentNullException.ThrowIfNull(grant);

        var secretId = grant.ItemKind == ShareItemKind.Secret ? grant.ItemId : (Guid?)null;
        return new VaultAuditItem(eventType, grant.OwnerId, grant.ItemId, secretId, grant.RecipientId, grant.Permission.ToString());
    }

    private static AuditEventType SharedEvent(ShareGrant grant) => grant.ItemKind switch
    {
        ShareItemKind.Secret => AuditEventType.SecretShared,
        ShareItemKind.Folder => AuditEventType.FolderShared,
        _ => throw new ArgumentOutOfRangeException(nameof(grant), grant.ItemKind, null),
    };

    private static AuditEventType ChangedEvent(ShareGrant grant) => grant.ItemKind switch
    {
        ShareItemKind.Secret => AuditEventType.SecretShareChanged,
        ShareItemKind.Folder => AuditEventType.FolderShareChanged,
        _ => throw new ArgumentOutOfRangeException(nameof(grant), grant.ItemKind, null),
    };

    private static AuditEventType RevokedEvent(ShareGrant grant) => grant.ItemKind switch
    {
        ShareItemKind.Secret => AuditEventType.SecretShareRevoked,
        ShareItemKind.Folder => AuditEventType.FolderShareRevoked,
        _ => throw new ArgumentOutOfRangeException(nameof(grant), grant.ItemKind, null),
    };
}
