// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.OneTimeLinks.Commands;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class RevealOneTimeLinkSnapshotCommandValidatorTests
{
    private static readonly RevealOneTimeLinkSnapshotCommandValidator Validator = new();

    [Fact]
    public void Validate_BothIdsSet_Passes()
    {
        Assert.True(Validator.Validate(new RevealOneTimeLinkSnapshotCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void Validate_EmptyOwnerId_Fails()
    {
        Assert.False(Validator.Validate(new RevealOneTimeLinkSnapshotCommand(Guid.Empty, Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void Validate_EmptySecretId_Fails()
    {
        Assert.False(Validator.Validate(new RevealOneTimeLinkSnapshotCommand(Guid.NewGuid(), Guid.Empty)).IsValid);
    }
}
