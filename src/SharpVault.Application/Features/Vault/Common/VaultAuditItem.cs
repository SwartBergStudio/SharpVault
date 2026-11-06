// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.Vault.Common;

public sealed record VaultAuditItem(
    AuditEventType EventType,
    Guid OwnerId,
    Guid TargetId,
    Guid? SecretId,
    Guid? RecipientId = null,
    string? SharePermission = null);
