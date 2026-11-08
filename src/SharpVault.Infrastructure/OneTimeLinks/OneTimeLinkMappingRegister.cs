// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.OneTimeLinks;

namespace SharpVault.Infrastructure.OneTimeLinks;

public sealed class OneTimeLinkMappingRegister : IRegister
{
    private const int CreatorPrefixLength = 2;
    private const int LinkPrefixLength = 2;

    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<OneTimeLink, OneTimeLinkTableEntity>().MapWith(link => ToEntity(link));
        config.NewConfig<OneTimeLinkTableEntity, OneTimeLink>().MapWith(entity => ToDomain(entity));
        config.NewConfig<OneTimeLinkIndexEntry, OneTimeLinkIndexTableEntity>().MapWith(index => ToEntity(index));
        config.NewConfig<OneTimeLinkIndexTableEntity, OneTimeLinkIndexEntry>().MapWith(entity => ToDomain(entity));
        config.NewConfig<OneTimeLinkNotice, OneTimeLinkNoticeTableEntity>().MapWith(notice => ToEntity(notice));
        config.NewConfig<OneTimeLinkNoticeTableEntity, OneTimeLinkNotice>().MapWith(entity => ToDomain(entity));
        config.NewConfig<OneTimeLinkExpiryEntry, OneTimeLinkExpiryTableEntity>().MapWith(entry => ToEntity(entry));
        config.NewConfig<OneTimeLinkExpiryTableEntity, OneTimeLinkExpiryEntry>().MapWith(entity => ToDomain(entity));
    }

    internal static OneTimeLinkTableEntity ToEntity(OneTimeLink link)
    {
        var (first, second) = OneTimeLinkPayloadColumns.Split(link.Payload);
        return new()
        {
            PartitionKey = OneTimeLinkKeys.LinkPartition(link.Id),
            RowKey = OneTimeLinkKeys.LinkRowKey,
            Payload0 = first,
            Payload1 = second,
            CreatorId = OneTimeLinkKeys.FormatId(link.CreatorId),
            OwnerId = OneTimeLinkKeys.FormatId(link.OwnerId),
            SecretId = OneTimeLinkKeys.FormatId(link.SecretId),
            CreatedAt = link.CreatedAt,
            ExpiresAt = link.ExpiresAt,
        };
    }

    internal static OneTimeLink ToDomain(OneTimeLinkTableEntity entity) => new()
    {
        Id = ParsePrefixed(entity.PartitionKey, OneTimeLinkKeys.LinkPartitionPrefix, OneTimeLinkKeys.ParseLinkId, LinkPrefixLength),
        CreatorId = OneTimeLinkKeys.ParseId(entity.CreatorId),
        OwnerId = OneTimeLinkKeys.ParseId(entity.OwnerId),
        SecretId = OneTimeLinkKeys.ParseId(entity.SecretId),
        Payload = OneTimeLinkPayloadColumns.Join(entity.Payload0, entity.Payload1),
        CreatedAt = entity.CreatedAt,
        ExpiresAt = entity.ExpiresAt,
    };

    internal static OneTimeLinkIndexTableEntity ToEntity(OneTimeLinkIndexEntry index) => new()
    {
        PartitionKey = OneTimeLinkKeys.CreatorPartition(index.CreatorId),
        RowKey = OneTimeLinkKeys.IndexRowKey(index.LinkId),
        OwnerId = OneTimeLinkKeys.FormatId(index.OwnerId),
        SecretId = OneTimeLinkKeys.FormatId(index.SecretId),
        NameEnvelope = index.NameEnvelope,
        MetadataKeyVersion = index.MetadataKeyVersion,
        CreatedAt = index.CreatedAt,
        ExpiresAt = index.ExpiresAt,
    };

    internal static OneTimeLinkIndexEntry ToDomain(OneTimeLinkIndexTableEntity entity) => new()
    {
        LinkId = ParsePrefixed(entity.RowKey, OneTimeLinkKeys.IndexPrefix, OneTimeLinkKeys.ParseLinkId, LinkPrefixLength),
        CreatorId = ParsePrefixed(entity.PartitionKey, OneTimeLinkKeys.CreatorPartitionPrefix, OneTimeLinkKeys.ParseId, CreatorPrefixLength),
        OwnerId = OneTimeLinkKeys.ParseId(entity.OwnerId),
        SecretId = OneTimeLinkKeys.ParseId(entity.SecretId),
        NameEnvelope = entity.NameEnvelope,
        MetadataKeyVersion = entity.MetadataKeyVersion,
        CreatedAt = entity.CreatedAt,
        ExpiresAt = entity.ExpiresAt,
    };

    internal static OneTimeLinkNoticeTableEntity ToEntity(OneTimeLinkNotice notice) => new()
    {
        PartitionKey = OneTimeLinkKeys.CreatorPartition(notice.CreatorId),
        RowKey = OneTimeLinkKeys.NoticeRowKey(notice.OpenedAt, notice.LinkId),
        LinkId = OneTimeLinkKeys.FormatId(notice.LinkId),
        OwnerId = OneTimeLinkKeys.FormatId(notice.OwnerId),
        SecretId = OneTimeLinkKeys.FormatId(notice.SecretId),
        NameEnvelope = notice.NameEnvelope,
        MetadataKeyVersion = notice.MetadataKeyVersion,
        OpenedAt = notice.OpenedAt,
    };

    internal static OneTimeLinkNotice ToDomain(OneTimeLinkNoticeTableEntity entity) => new()
    {
        NoticeId = entity.RowKey,
        LinkId = OneTimeLinkKeys.ParseId(entity.LinkId),
        CreatorId = ParsePrefixed(entity.PartitionKey, OneTimeLinkKeys.CreatorPartitionPrefix, OneTimeLinkKeys.ParseId, CreatorPrefixLength),
        OwnerId = OneTimeLinkKeys.ParseId(entity.OwnerId),
        SecretId = OneTimeLinkKeys.ParseId(entity.SecretId),
        NameEnvelope = entity.NameEnvelope,
        MetadataKeyVersion = entity.MetadataKeyVersion,
        OpenedAt = entity.OpenedAt,
    };

    internal static OneTimeLinkExpiryTableEntity ToEntity(OneTimeLinkExpiryEntry entry) => new()
    {
        PartitionKey = OneTimeLinkKeys.ExpiryPartition(entry.ExpiresAt),
        RowKey = OneTimeLinkKeys.ExpiryRowKey(entry.LinkId),
        CreatorId = OneTimeLinkKeys.FormatId(entry.CreatorId),
        ExpiresAt = entry.ExpiresAt,
    };

    internal static OneTimeLinkExpiryEntry ToDomain(OneTimeLinkExpiryTableEntity entity) => new()
    {
        LinkId = OneTimeLinkKeys.ParseLinkId(entity.RowKey),
        CreatorId = OneTimeLinkKeys.ParseId(entity.CreatorId),
        ExpiresAt = entity.ExpiresAt,
    };

    private static Guid ParsePrefixed(string text, string prefix, Func<string, Guid> parse, int prefixLength) =>
        text.StartsWith(prefix, StringComparison.Ordinal)
            ? parse(text[prefixLength..])
            : throw new InvalidDataException(OneTimeLinkStorageMessages.InvalidKey);
}
