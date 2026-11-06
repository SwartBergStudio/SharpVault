// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Sharing.Grants.Commands;
using SharpVault.Domain.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

public sealed class RevokeShareGrantCommandValidatorTests
{
    private readonly RevokeShareGrantCommandValidator _validator = new();

    private static RevokeShareGrantCommand Valid() =>
        new(Guid.NewGuid(), ShareItemKind.Folder, Guid.NewGuid(), Guid.NewGuid());

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public void Validate_ValidCommand_Passes(ShareItemKind kind)
    {
        Assert.True(_validator.Validate(Valid() with { Kind = kind }).IsValid);
    }

    [Fact]
    public void Validate_EmptyOwnerId_Fails()
    {
        Assert.False(_validator.Validate(Valid() with { OwnerId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_EmptyItemId_Fails()
    {
        Assert.False(_validator.Validate(Valid() with { ItemId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_EmptyRecipientId_Fails()
    {
        Assert.False(_validator.Validate(Valid() with { RecipientId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_KindOutsideTheEnum_Fails()
    {
        Assert.False(_validator.Validate(Valid() with { Kind = (ShareItemKind)9 }).IsValid);
    }
}
