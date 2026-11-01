// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.Infrastructure.Audit;

namespace SharpVault.UnitTests.Infrastructure.Audit;

public sealed partial class AuditKeysTests
{
    [GeneratedRegex("^[0-9]{19}_[0-9a-f]{32}$")]
    private static partial Regex RowKeyPattern();

    [Fact]
    public void PartitionKey_UsesUtcYearAndMonth()
    {
        var justAfterMidnightLocal = new DateTimeOffset(2026, 1, 1, 0, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("202512", AuditKeys.PartitionKey(justAfterMidnightLocal));
    }

    [Fact]
    public void PartitionKey_UtcTime_UsesSameMonth()
    {
        Assert.Equal("202603", AuditKeys.PartitionKey(new DateTimeOffset(2026, 3, 31, 23, 59, 59, TimeSpan.Zero)));
    }

    [Fact]
    public void PartitionKey_NegativeOffsetPastMonthEnd_RollsForwardToNextUtcMonth()
    {
        var local = new DateTimeOffset(2026, 3, 31, 22, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal("202604", AuditKeys.PartitionKey(local));
    }

    [Fact]
    public void RowKey_IsNineteenDigitsUnderscoreAndThirtyTwoHex()
    {
        var key = AuditKeys.RowKey(DateTimeOffset.UtcNow, Guid.NewGuid());

        Assert.Matches(RowKeyPattern(), key);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RowKey_ExtremeInstants_StayNineteenDigits(int extreme)
    {
        var instant = extreme == 0 ? DateTimeOffset.MinValue : DateTimeOffset.MaxValue;

        Assert.Matches(RowKeyPattern(), AuditKeys.RowKey(instant, Guid.NewGuid()));
    }

    [Fact]
    public void RowKey_NewerEvent_SortsBeforeOlderEvent()
    {
        var older = AuditKeys.RowKey(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), Guid.NewGuid());
        var newer = AuditKeys.RowKey(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero), Guid.NewGuid());

        Assert.True(string.CompareOrdinal(newer, older) < 0);
    }

    [Fact]
    public void RowKey_OneTickApart_SortsNewestFirst()
    {
        var instant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var older = AuditKeys.RowKey(instant, Guid.NewGuid());
        var newer = AuditKeys.RowKey(instant.AddTicks(1), Guid.NewGuid());

        Assert.True(string.CompareOrdinal(newer, older) < 0);
    }

    [Fact]
    public void RowKey_SameInstantDifferentIds_AreDistinct()
    {
        var instant = DateTimeOffset.UtcNow;

        Assert.NotEqual(AuditKeys.RowKey(instant, Guid.NewGuid()), AuditKeys.RowKey(instant, Guid.NewGuid()));
    }

    [Fact]
    public void RowKey_SameInstantAndId_IsDeterministic()
    {
        var instant = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        Assert.Equal(AuditKeys.RowKey(instant, id), AuditKeys.RowKey(instant, id));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Keys_Always_ContainNoCharactersIllegalInTableKeys()
    {
        var instant = DateTimeOffset.UtcNow;
        var keys = new[] { AuditKeys.PartitionKey(instant), AuditKeys.RowKey(instant, Guid.NewGuid()) };

        Assert.All(keys, key => Assert.DoesNotContain(key, character => "/\\#?".Contains(character) || char.IsControl(character)));
    }
}
