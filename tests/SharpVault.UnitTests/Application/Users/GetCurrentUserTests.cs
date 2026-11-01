// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Queries;

namespace SharpVault.UnitTests.Application.Users;

public sealed class GetCurrentUserTests
{
    private static readonly string[] ExpectedProperties = ["DisplayName", "IsAdmin", "UserId"];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_SignedInUser_ReturnsIdNameAndAdminFlagFromCurrentUser(bool isAdmin)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        var userId = Guid.NewGuid();
        currentUser.UserId.Returns(userId);
        currentUser.DisplayName.Returns("Alice Example");
        currentUser.IsAdmin.Returns(isAdmin);

        var result = await new GetCurrentUserQueryHandler(currentUser)
            .Handle(new GetCurrentUserQuery(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value!.UserId);
        Assert.Equal("Alice Example", result.Value.DisplayName);
        Assert.Equal(isAdmin, result.Value.IsAdmin);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CurrentUserDto_Properties_AreIdNameAndAdminFlagOnly()
    {
        var names = typeof(SharpVault.Application.Features.Users.Dtos.CurrentUserDto)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(ExpectedProperties, names);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ICurrentUser_Members_HaveNoEmail()
    {
        var names = typeof(ICurrentUser).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(ExpectedProperties, names);
    }
}
