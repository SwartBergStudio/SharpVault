// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Sharing.Grants.Commands;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

public sealed class ChangeShareGrantCommandValidatorTests
{
    private const string ValidETag = "W/\"datetime'2026-03-01T09%3A00%3A01.0000000Z'\"";

    private readonly ChangeShareGrantCommandValidator _validator = new();

    private static ChangeShareGrantCommand Valid() =>
        new(Guid.NewGuid(), ShareItemKind.Secret, Guid.NewGuid(), Guid.NewGuid(), ValidETag, SharePermissionDto.Edit);

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        Assert.True(_validator.Validate(Valid()).IsValid);
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
    public void Validate_PermissionOutsideTheEnum_Fails(int value)
    {
        Assert.False(_validator.Validate(Valid() with { Permission = (SharePermissionDto)value }).IsValid);
    }

    [Fact]
    public void Validate_KindOutsideTheEnum_Fails()
    {
        Assert.False(_validator.Validate(Valid() with { Kind = (ShareItemKind)7 }).IsValid);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("\"abc\"")]
    [InlineData("W/\"datetime'abc'\"\n")]
    [InlineData("W/\"datetime'a'\" or 1=1")]
    public void Validate_MalformedETag_Fails(string etag)
    {
        Assert.False(_validator.Validate(Valid() with { ETag = etag }).IsValid);
    }

    [Fact]
    public void Validate_OversizedETag_Fails()
    {
        var etag = "W/\"datetime'" + new string('a', 500) + "'\"";

        Assert.False(_validator.Validate(Valid() with { ETag = etag }).IsValid);
    }
}
