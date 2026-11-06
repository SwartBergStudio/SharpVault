// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Sharing.Grants.Queries;
using SharpVault.Domain.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Grants;

public sealed class ListShareGrantsQueryValidatorTests
{
    private readonly ListShareGrantsQueryValidator _validator = new();

    [Theory]
    [InlineData(ShareItemKind.Secret)]
    [InlineData(ShareItemKind.Folder)]
    public void Validate_ValidQuery_Passes(ShareItemKind kind)
    {
        Assert.True(_validator.Validate(new ListShareGrantsQuery(Guid.NewGuid(), kind, Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void Validate_EmptyOwnerId_Fails()
    {
        Assert.False(_validator.Validate(new ListShareGrantsQuery(Guid.Empty, ShareItemKind.Secret, Guid.NewGuid())).IsValid);
    }

    [Fact]
    public void Validate_EmptyItemId_Fails()
    {
        Assert.False(_validator.Validate(new ListShareGrantsQuery(Guid.NewGuid(), ShareItemKind.Secret, Guid.Empty)).IsValid);
    }

    [Fact]
    public void Validate_KindOutsideTheEnum_Fails()
    {
        Assert.False(_validator.Validate(new ListShareGrantsQuery(Guid.NewGuid(), (ShareItemKind)5, Guid.NewGuid())).IsValid);
    }
}
