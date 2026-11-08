// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public sealed record OneTimeLinkAuditItem(
    AuditEventType EventType,
    Guid ActorId,
    Guid TargetId,
    Guid OwnerId,
    Guid SecretId,
    string? IpAddress,
    string? UserAgent);
