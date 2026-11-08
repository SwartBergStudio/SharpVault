// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Infrastructure.OneTimeLinks;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkPayloadColumnsTests
{
    private const int ColumnBytes = 65536;

    [Fact]
    public void Split_AtTheColumnLimit_UsesOneColumn()
    {
        var payload = new byte[ColumnBytes];

        var (first, second) = OneTimeLinkPayloadColumns.Split(payload);

        Assert.Equal(ColumnBytes, first.Length);
        Assert.Null(second);
    }

    [Fact]
    public void Split_OneByteOver_UsesTwoColumns()
    {
        var payload = new byte[ColumnBytes + 1];

        var (first, second) = OneTimeLinkPayloadColumns.Split(payload);

        Assert.Equal(ColumnBytes, first.Length);
        Assert.NotNull(second);
        Assert.Single(second);
    }

    [Fact]
    public void Split_OverTheTwoColumnLimit_Throws()
    {
        var payload = new byte[(ColumnBytes * 2) + 1];

        Assert.Throws<ArgumentException>(() => OneTimeLinkPayloadColumns.Split(payload));
    }

    [Fact]
    public void Join_RestoresTheBytes()
    {
        var payload = Enumerable.Range(0, ColumnBytes + 1000).Select(index => (byte)(index % 251)).ToArray();
        var (first, second) = OneTimeLinkPayloadColumns.Split(payload);

        var joined = OneTimeLinkPayloadColumns.Join(first, second);

        Assert.Equal(payload, joined);
    }
}
