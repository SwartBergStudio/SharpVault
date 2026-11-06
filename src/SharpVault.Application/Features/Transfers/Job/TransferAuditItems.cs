// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Transfers;

namespace SharpVault.Application.Features.Transfers.Job;

public static class TransferAuditItems
{

    public static AdminAuditItem Approved(TransferJob job) => new(
        AuditEventType.TransferApproved,
        job.DecidedBy ?? AuditActors.System,
        job.SourceUserId,
        job.TargetUserId,
        job.Id,
        job.Category.ToString(),
        job.Reason,
        job.ApprovedFromIp);

    public static AdminAuditItem Started(TransferJob job) => System(job, AuditEventType.TransferStarted);

    public static AdminAuditItem Blocked(TransferJob job, TransferBlockReason reason) => System(job, AuditEventType.TransferBlocked, reason.ToString());

    public static AdminAuditItem Resumed(TransferJob job) => System(job, AuditEventType.TransferResumed);

    public static AdminAuditItem Expired(TransferJob job) => System(job, AuditEventType.TransferExpired);

    public static AdminAuditItem ReleaseStarted(TransferJob job) => System(job, AuditEventType.TransferReleaseStarted);

    public static AdminAuditItem Completed(TransferJob job) => System(
        job,
        AuditEventType.TransferCompleted,
        $"items={job.ItemCount};folders={job.FolderCount};skipped={job.SkippedItemCount}");

    public static AdminAuditItem Transferred(TransferJob job, AuditEventType eventType, Guid itemId, bool isSecret) => new(
        eventType,
        AuditActors.System,
        job.TargetUserId,
        itemId,
        job.Id,
        null,
        null,
        null,
        SecretId: isSecret ? itemId : null);

    public static AdminAuditItem RenamedForTarget(TransferJob job, Guid secretId, Guid newSecretId) => new(
        AuditEventType.SecretTransferredUnderNewId,
        AuditActors.System,
        job.TargetUserId,
        newSecretId,
        job.Id,
        null,
        null,
        null,
        SecretId: secretId);

    public static AdminAuditItem Skipped(TransferJob job, Guid secretId) => new(
        AuditEventType.TransferItemSkipped,
        AuditActors.System,
        job.SourceUserId,
        secretId,
        job.Id,
        null,
        null,
        null);

    public static AdminAuditItem Reassigned(TransferJob job, ShareGrant grant, Guid targetItemId) => new(
        AuditEventType.ShareReassigned,
        AuditActors.System,
        job.TargetUserId,
        targetItemId,
        job.Id,
        null,
        null,
        null,
        SecretId: grant.ItemKind == ShareItemKind.Secret ? targetItemId : null,
        RecipientId: grant.RecipientId,
        SharePermission: grant.Permission.ToString());

    public static AdminAuditItem RevokedByTransfer(TransferJob job, ShareGrant grant) => new(
        AuditEventType.ShareRevokedByTransfer,
        AuditActors.System,
        job.TargetUserId,
        grant.ItemId,
        job.Id,
        null,
        null,
        null,
        SecretId: grant.ItemKind == ShareItemKind.Secret ? grant.ItemId : null,
        RecipientId: grant.RecipientId);

    private static AdminAuditItem System(TransferJob job, AuditEventType eventType, string? reason = null) => new(
        eventType,
        AuditActors.System,
        job.SourceUserId,
        job.TargetUserId,
        job.Id,
        job.Category.ToString(),
        reason,
        null);
}
