// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Application.Mapping;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Users;

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
    [InlineData(UserStatus.Active, UserStatusDto.Active)]
    [InlineData(UserStatus.Locked, UserStatusDto.Locked)]
    public void Adapt_User_MapsToSummaryWithStatus(UserStatus status, UserStatusDto expected)
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
        Assert.Equal(expected, summary.Status);
        Assert.Equal(user.LastSignInAt, summary.LastSignInAt);
    }

    [Fact]
    public void Map_EveryUserStatus_MapsToTheMatchingUserStatusDto()
    {
        var statuses = Enum.GetValues<UserStatus>();

        foreach (var status in statuses)
        {
            var summary = new User { Id = Guid.NewGuid(), Status = status }.Adapt<UserSummaryDto>(ScannedConfig());

            Assert.Equal(status.ToString(), summary.Status.ToString());
        }

        Assert.Equal(Enum.GetNames<UserStatus>().Order(StringComparer.Ordinal), Enum.GetNames<UserStatusDto>().Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Map_UnknownUserStatus_Throws()
    {
        var user = new User { Id = Guid.NewGuid(), Status = (UserStatus)999 };

        Assert.Throws<ArgumentOutOfRangeException>(() => user.Adapt<UserSummaryDto>(ScannedConfig()));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UserStatusDto_DefaultValue_IsLocked()
    {
        Assert.Equal(UserStatusDto.Locked, default(UserStatusDto));
        Assert.Equal(UserStatusDto.Locked, new UserSummaryDto().Status);
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
