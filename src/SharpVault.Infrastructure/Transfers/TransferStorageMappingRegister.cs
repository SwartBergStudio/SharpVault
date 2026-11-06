// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text.Json;
using Mapster;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;

namespace SharpVault.Infrastructure.Transfers;

public sealed class TransferStorageMappingRegister : IRegister
{
    private const string IdFormat = "D";

    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<TransferJob, TransferJobTableEntity>().MapWith(job => ToEntity(job));
        config.NewConfig<TransferJobTableEntity, TransferJob>().MapWith(entity => ToDomain(entity));
        config.NewConfig<TransferMarker, TransferMarkerTableEntity>().MapWith(marker => ToEntity(marker));
        config.NewConfig<TransferMarkerTableEntity, TransferMarker>().MapWith(entity => ToDomain(entity));
        config.NewConfig<TransferNotice, TransferNoticeTableEntity>().MapWith(notice => ToEntity(notice));
        config.NewConfig<TransferNoticeTableEntity, TransferNotice>().MapWith(entity => ToDomain(entity));
    }

    internal static TransferJobTableEntity ToEntity(TransferJob job) => new()
    {
        PartitionKey = TransferKeys.TransferPartition,
        RowKey = TransferKeys.JobRowKey(job.Id),
        State = job.State.ToString(),
        SourceUserId = Format(job.SourceUserId),
        TargetUserId = Format(job.TargetUserId),
        SourceDisplayName = job.SourceDisplayName,
        TargetDisplayName = job.TargetDisplayName,
        ScopeKind = job.ScopeKind.ToString(),
        SelectedFolderIds = JsonSerializer.Serialize(job.SelectedFolderIds.Select(Format)),
        Category = job.Category.ToString(),
        Reason = job.Reason,
        RequestedBy = Format(job.RequestedBy),
        RequestedByName = job.RequestedByName,
        RequestedAt = job.RequestedAt,
        ExpiresAt = job.ExpiresAt,
        DecidedBy = job.DecidedBy is { } decidedBy ? Format(decidedBy) : null,
        DecidedByName = job.DecidedByName,
        DecidedAt = job.DecidedAt,
        DecisionReason = job.DecisionReason,
        ApprovedFromIp = job.ApprovedFromIp,
        ApprovalAudited = job.ApprovalAudited,
        RootFolderId = job.RootFolderId is { } rootFolderId ? Format(rootFolderId) : null,
        StartedAt = job.StartedAt,
        ReleaseStartedAt = job.ReleaseStartedAt,
        CompletedAt = job.CompletedAt,
        BlockReason = job.BlockReason?.ToString(),
        Attempts = job.Attempts,
        NextAttemptAt = job.NextAttemptAt,
        LastFailureKind = job.LastFailureKind,
        CleanupPending = job.CleanupPending,
        ItemCount = job.ItemCount,
        FolderCount = job.FolderCount,
        VersionCount = job.VersionCount,
        RecycledItemCount = job.RecycledItemCount,
        SkippedItemCount = job.SkippedItemCount,
        PublishedItemCount = job.PublishedItemCount,
        RenamedItemCount = job.RenamedItemCount,
        FailingSince = job.FailingSince,
    };

    internal static TransferJob ToDomain(TransferJobTableEntity entity) => new()
    {
        Id = TransferKeys.JobIdFromRowKey(entity.RowKey) ?? throw new InvalidDataException("A transfer job row has an invalid key."),
        State = ParseEnum<TransferState>(entity.State),
        SourceUserId = ParseId(entity.SourceUserId),
        TargetUserId = ParseId(entity.TargetUserId),
        SourceDisplayName = entity.SourceDisplayName,
        TargetDisplayName = entity.TargetDisplayName,
        ScopeKind = ParseEnum<TransferScopeKind>(entity.ScopeKind),
        SelectedFolderIds = ParseFolderIds(entity.SelectedFolderIds),
        Category = ParseEnum<ReasonCategory>(entity.Category),
        Reason = entity.Reason,
        RequestedBy = ParseId(entity.RequestedBy),
        RequestedByName = entity.RequestedByName,
        RequestedAt = entity.RequestedAt,
        ExpiresAt = entity.ExpiresAt,
        DecidedBy = ParseOptionalId(entity.DecidedBy),
        DecidedByName = entity.DecidedByName,
        DecidedAt = entity.DecidedAt,
        DecisionReason = entity.DecisionReason,
        ApprovedFromIp = entity.ApprovedFromIp,
        ApprovalAudited = entity.ApprovalAudited,
        RootFolderId = ParseOptionalId(entity.RootFolderId),
        StartedAt = entity.StartedAt,
        ReleaseStartedAt = entity.ReleaseStartedAt,
        CompletedAt = entity.CompletedAt,
        BlockReason = entity.BlockReason is null ? null : ParseEnum<TransferBlockReason>(entity.BlockReason),
        Attempts = entity.Attempts,
        NextAttemptAt = entity.NextAttemptAt,
        LastFailureKind = entity.LastFailureKind,
        CleanupPending = entity.CleanupPending,
        ItemCount = entity.ItemCount,
        FolderCount = entity.FolderCount,
        VersionCount = entity.VersionCount,
        RecycledItemCount = entity.RecycledItemCount,
        SkippedItemCount = entity.SkippedItemCount,
        PublishedItemCount = entity.PublishedItemCount,
        RenamedItemCount = entity.RenamedItemCount,
        FailingSince = entity.FailingSince,
    };

    internal static TransferMarkerTableEntity ToEntity(TransferMarker marker) => new()
    {
        PartitionKey = TransferKeys.TransferPartition,
        RowKey = TransferKeys.MarkerRowKey(marker.SourceUserId),
        TransferId = Format(marker.TransferId),
        TargetUserId = Format(marker.TargetUserId),
    };

    internal static TransferMarker ToDomain(TransferMarkerTableEntity entity) => new()
    {
        SourceUserId = Guid.ParseExact(entity.RowKey[TransferKeys.MarkerPrefix.Length..], "N"),
        TransferId = ParseId(entity.TransferId),
        TargetUserId = ParseId(entity.TargetUserId),
    };

    internal static TransferNoticeTableEntity ToEntity(TransferNotice notice) => new()
    {
        PartitionKey = TransferKeys.NoticePartition(notice.UserId),
        RowKey = TransferKeys.NoticeRowKey(notice.TransferId),
        TransferId = Format(notice.TransferId),
        Kind = notice.Kind.ToString(),
        SourceDisplayName = notice.SourceDisplayName,
        CompletedAt = notice.CompletedAt,
        ItemCount = notice.ItemCount,
        FolderId = notice.FolderId is { } folderId ? Format(folderId) : null,
    };

    internal static TransferNotice ToDomain(TransferNoticeTableEntity entity) => new()
    {
        UserId = Guid.ParseExact(entity.PartitionKey[TransferKeys.NoticePartitionPrefix.Length..], IdFormat),
        TransferId = ParseId(entity.TransferId),
        Kind = ParseEnum<TransferNoticeKind>(entity.Kind),
        SourceDisplayName = entity.SourceDisplayName,
        CompletedAt = entity.CompletedAt,
        ItemCount = entity.ItemCount,
        FolderId = ParseOptionalId(entity.FolderId),
    };

    private static string Format(Guid id) => id.ToString(IdFormat, CultureInfo.InvariantCulture);

    private static Guid ParseId(string text) =>
        Guid.TryParseExact(text, IdFormat, out var id) ? id : throw new InvalidDataException("A transfer row holds an invalid id.");

    private static Guid? ParseOptionalId(string? text) => text is null ? null : ParseId(text);

    private static T ParseEnum<T>(string text)
        where T : struct, Enum =>
        Enum.TryParse<T>(text, ignoreCase: false, out var value) && string.Equals(Enum.GetName(value), text, StringComparison.Ordinal)
            ? value
            : throw new InvalidDataException("A transfer row holds an invalid value.");

    private static List<Guid> ParseFolderIds(string json)
    {
        var texts = JsonSerializer.Deserialize<List<string>>(json) ?? [];
        return [.. texts.Select(ParseId)];
    }
}
