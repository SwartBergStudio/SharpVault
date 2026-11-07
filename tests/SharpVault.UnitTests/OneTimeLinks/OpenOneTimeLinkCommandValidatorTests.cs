// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.OneTimeLinks.Commands;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OpenOneTimeLinkCommandValidatorTests
{
    private static readonly OpenOneTimeLinkCommandValidator Validator = new();

    [Fact]
    public void Validate_LinkIdSet_Passes()
    {
        Assert.True(Validator.Validate(new OpenOneTimeLinkCommand(Guid.NewGuid(), "203.0.113.5", "agent")).IsValid);
    }

    [Fact]
    public void Validate_EmptyLinkId_Fails()
    {
        Assert.False(Validator.Validate(new OpenOneTimeLinkCommand(Guid.Empty, null, null)).IsValid);
    }
}
