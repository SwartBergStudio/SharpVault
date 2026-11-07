// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Infrastructure.OneTimeLinks;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkKeysTests
{
    private const string IndexRangeStart = "a_";
    private const string NoticeRangeStart = "n_";

    private static readonly Guid LinkId = new("0c5a7e3b-1f2d-4a6b-9c8e-7d6f5e4d3c2b");

    [Fact]
    public void ExpiryPartition_UsesTheUtcHour()
    {
        var expiresAt = new DateTimeOffset(2026, 11, 7, 12, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("e_2026110710", OneTimeLinkKeys.ExpiryPartition(expiresAt));
    }

    [Fact]
    public void Ranges_CoverIndexRowsAndExcludeNoticeRows()
    {
        var indexRow = OneTimeLinkKeys.IndexRowKey(LinkId);
        var noticeRow = OneTimeLinkKeys.NoticeRowKey(DateTimeOffset.UtcNow, LinkId);

        Assert.True(string.CompareOrdinal(indexRow, IndexRangeStart) >= 0);
        Assert.True(string.CompareOrdinal(indexRow, OneTimeLinkKeys.IndexUpperBound) < 0);
        Assert.False(string.CompareOrdinal(noticeRow, OneTimeLinkKeys.IndexUpperBound) < 0 && string.CompareOrdinal(noticeRow, IndexRangeStart) >= 0);
    }

    [Fact]
    public void Ranges_CoverNoticeRowsAndExcludeIndexRows()
    {
        var indexRow = OneTimeLinkKeys.IndexRowKey(LinkId);
        var noticeRow = OneTimeLinkKeys.NoticeRowKey(DateTimeOffset.UtcNow, LinkId);

        Assert.True(string.CompareOrdinal(noticeRow, NoticeRangeStart) >= 0);
        Assert.True(string.CompareOrdinal(noticeRow, OneTimeLinkKeys.NoticeUpperBound) < 0);
        Assert.False(string.CompareOrdinal(indexRow, OneTimeLinkKeys.NoticeUpperBound) < 0 && string.CompareOrdinal(indexRow, NoticeRangeStart) >= 0);
    }
}
