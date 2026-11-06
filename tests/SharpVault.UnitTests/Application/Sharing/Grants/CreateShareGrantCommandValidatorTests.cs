// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Sharing.Grants.Commands;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

public sealed class CreateShareGrantCommandValidatorTests
{
    private readonly CreateShareGrantCommandValidator _validator = new();

    private static CreateShareGrantCommand Valid() =>
        new(Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), Guid.NewGuid(), SharePermissionDto.View);

    [Theory]
    [InlineData(ShareItemKind.Secret, SharePermissionDto.View)]
    [InlineData(ShareItemKind.Secret, SharePermissionDto.Edit)]
    [InlineData(ShareItemKind.Folder, SharePermissionDto.Manage)]
    public void Validate_ValidCommand_Passes(ShareItemKind kind, SharePermissionDto permission)
    {
        var result = _validator.Validate(Valid() with { Kind = kind, Permission = permission });

        Assert.True(result.IsValid);
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

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    [InlineData(99)]
    public void Validate_PermissionOutsideTheEnum_Fails(int value)
    {
        Assert.False(_validator.Validate(Valid() with { Permission = (SharePermissionDto)value }).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(99)]
    public void Validate_KindOutsideTheEnum_Fails(int value)
    {
        Assert.False(_validator.Validate(Valid() with { Kind = (ShareItemKind)value }).IsValid);
    }
}
