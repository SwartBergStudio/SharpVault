// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Audit;

public sealed class AuditEntry
{
    public Guid Id { get; set; }

    public AuditEventType EventType { get; set; }

    public Guid ActorId { get; set; }

    public string? TargetId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string? IpAddress { get; set; }

    public string? Reason { get; set; }

    public Guid? OwnerId { get; set; }

    public Guid? SecretId { get; set; }

    public Guid? RecipientId { get; set; }

    public string? SharePermission { get; set; }

    public Guid? TransferId { get; set; }

    public string? ReasonCategory { get; set; }

    public string? UserAgent { get; set; }
}
