// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.Users.Common;

public sealed record AdminAuditItem(
    AuditEventType EventType,
    Guid ActorId,
    Guid? OwnerId,
    Guid? TargetId,
    Guid? TransferId,
    string? ReasonCategory,
    string? Reason,
    string? IpAddress,
    Guid? SecretId = null,
    Guid? RecipientId = null,
    string? SharePermission = null);
