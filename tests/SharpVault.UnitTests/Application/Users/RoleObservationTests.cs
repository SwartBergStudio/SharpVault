// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Users.Access;

namespace SharpVault.UnitTests.Application.Users;

[Trait("Category", "Security")]
public sealed class RoleObservationTests
{
    private static readonly DateTimeOffset Recorded = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsNewer_TokenWithoutIssuedAt_IsFalse()
    {
        Assert.False(RoleObservation.IsNewer(true, Recorded, false, null));
        Assert.False(RoleObservation.IsNewer(false, null, true, null));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void IsNewer_SameFlag_IsFalse(bool stored, bool token)
    {
        Assert.False(RoleObservation.IsNewer(stored, Recorded, token, Recorded.AddHours(1)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsNewer_NothingRecordedYet_IsTrue(bool stored, bool token)
    {
        Assert.True(RoleObservation.IsNewer(stored, null, token, Recorded));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsNewer_TokenIssuedLater_IsTrue(bool stored, bool token)
    {
        Assert.True(RoleObservation.IsNewer(stored, Recorded, token, Recorded.AddSeconds(1)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsNewer_TokenIssuedEarlier_IsFalse(bool stored, bool token)
    {
        Assert.False(RoleObservation.IsNewer(stored, Recorded, token, Recorded.AddSeconds(-1)));
    }

    [Fact]
    public void IsNewer_SameInstantTokenSaysNotAdmin_DemotionWinsTheTie()
    {
        Assert.True(RoleObservation.IsNewer(true, Recorded, false, Recorded));
    }

    [Fact]
    public void IsNewer_SameInstantTokenSaysAdmin_IsFalse()
    {
        Assert.False(RoleObservation.IsNewer(false, Recorded, true, Recorded));
    }
}
