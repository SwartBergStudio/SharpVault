// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Application.Features.Users.Dtos;
using SharpVault.Application.Mapping;
using SharpVault.Domain.Users;

namespace SharpVault.UnitTests.Application.Mapping;

public sealed class UserMappingRegisterTests
{
    private static readonly string[] DirectoryProperties = ["DisplayName", "Email", "LastSignInAt", "Status", "UserId"];

    private static TypeAdapterConfig ScannedConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(UserMappingRegister).Assembly);
        return config;
    }

    [Theory]
    [InlineData(UserStatus.Active, "Active")]
    [InlineData(UserStatus.Locked, "Locked")]
    public void Adapt_User_MapsToSummaryWithStatusName(UserStatus status, string expectedName)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            DisplayName = "Alice Example",
            Email = "alice@example.test",
            Status = status,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastSignInAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        };

        var summary = user.Adapt<UserSummaryDto>(ScannedConfig());

        Assert.Equal(user.Id, summary.UserId);
        Assert.Equal("Alice Example", summary.DisplayName);
        Assert.Equal("alice@example.test", summary.Email);
        Assert.Equal(expectedName, summary.Status);
        Assert.Equal(user.LastSignInAt, summary.LastSignInAt);
    }

    [Fact]
    public void Adapt_UserWithoutEmail_MapsNullEmail()
    {
        var user = new User { Id = Guid.NewGuid(), Status = UserStatus.Active };

        var summary = user.Adapt<UserSummaryDto>(ScannedConfig());

        Assert.Null(summary.Email);
        Assert.Equal(user.Id, summary.UserId);
    }

    [Fact]
    public void Compile_ApplicationMappings_Succeeds()
    {
        var config = ScannedConfig();

        var exception = Record.Exception(() => config.Compile());

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UserSummaryDto_Properties_AreDirectoryStatusAndLastSignInOnly()
    {
        var names = typeof(UserSummaryDto).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(DirectoryProperties, names);
    }
}
