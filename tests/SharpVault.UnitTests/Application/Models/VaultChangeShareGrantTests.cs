// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Models;

public sealed class VaultChangeShareGrantTests
{
    private const string ETag = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";

    private readonly ShareGrant _grant = ShareSamples.Grant(Guid.NewGuid());

    [Fact]
    public void AddShareGrant_CarriesTheGrantAndItemIdWithoutETags()
    {
        var change = VaultChange.AddShareGrant(_grant);

        Assert.Equal(VaultChangeKind.AddShareGrant, change.Kind);
        Assert.Same(_grant, change.Grant);
        Assert.Equal(_grant.ItemId, change.ItemId);
        Assert.Null(change.ETag);
        Assert.Null(change.SecondaryETag);
    }

    [Fact]
    public void ReplaceShareGrant_CarriesBothETags()
    {
        var change = VaultChange.ReplaceShareGrant(_grant, ETag, "second");

        Assert.Equal(VaultChangeKind.ReplaceShareGrant, change.Kind);
        Assert.Equal(ETag, change.ETag);
        Assert.Equal("second", change.SecondaryETag);
        Assert.Same(_grant, change.Grant);
    }

    [Fact]
    public void DeleteShareGrant_CarriesBothETags()
    {
        var change = VaultChange.DeleteShareGrant(_grant, ETag, "second");

        Assert.Equal(VaultChangeKind.DeleteShareGrant, change.Kind);
        Assert.Equal(ETag, change.ETag);
        Assert.Equal("second", change.SecondaryETag);
    }

    [Fact]
    public void VaultChange_ShareGrantFactories_RejectNullGrants()
    {
        Assert.Throws<ArgumentNullException>(() => VaultChange.AddShareGrant(null!));
        Assert.Throws<ArgumentNullException>(() => VaultChange.ReplaceShareGrant(null!, ETag, ETag));
        Assert.Throws<ArgumentNullException>(() => VaultChange.DeleteShareGrant(null!, ETag, ETag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void VaultChange_ShareGrantFactories_RejectNullAndBlankETags(string? blank)
    {
        Assert.ThrowsAny<ArgumentException>(() => VaultChange.ReplaceShareGrant(_grant, blank!, ETag));
        Assert.ThrowsAny<ArgumentException>(() => VaultChange.ReplaceShareGrant(_grant, ETag, blank!));
        Assert.ThrowsAny<ArgumentException>(() => VaultChange.DeleteShareGrant(_grant, blank!, ETag));
        Assert.ThrowsAny<ArgumentException>(() => VaultChange.DeleteShareGrant(_grant, ETag, blank!));
    }

    [Fact]
    public void VaultChange_ExistingFactories_LeaveGrantAndSecondaryETagEmpty()
    {
        var change = VaultChange.TouchKeyRing(ETag);

        Assert.Null(change.Grant);
        Assert.Null(change.SecondaryETag);
    }
}
