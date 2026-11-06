// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Mapster;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Users;

namespace SharpVault.UnitTests.Infrastructure.Users;

public sealed class UserStorageMappingTests
{
    private static TypeAdapterConfig ScannedConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(UserKeys).Assembly);
        return config;
    }

    private static User NewUser(UserStatus status = UserStatus.Active) =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "Alice Example",
            Email = "alice@example.test",
            Status = status,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero),
            LastSignInAt = new DateTimeOffset(2026, 2, 2, 9, 30, 0, TimeSpan.Zero),
        };

    [Fact]
    public void Adapt_UserToEntity_SetsPartitionAndCanonicalRowKey()
    {
        var user = NewUser();

        var entity = user.Adapt<UserTableEntity>(ScannedConfig());

        Assert.Equal("user", entity.PartitionKey);
        Assert.Equal(user.Id.ToString("D"), entity.RowKey);
        Assert.Equal(entity.RowKey, entity.RowKey.ToLowerInvariant());
    }

    [Fact]
    public void Adapt_EntityToUser_RoundTripsEveryField()
    {
        var user = NewUser();
        var config = ScannedConfig();

        var roundTripped = user.Adapt<UserTableEntity>(config).Adapt<User>(config);

        Assert.Equal(user.Id, roundTripped.Id);
        Assert.Equal(user.DisplayName, roundTripped.DisplayName);
        Assert.Equal(user.Email, roundTripped.Email);
        Assert.Equal(user.Status, roundTripped.Status);
        Assert.Equal(user.CreatedAt, roundTripped.CreatedAt);
        Assert.Equal(user.LastSignInAt, roundTripped.LastSignInAt);
    }

    [Fact]
    public void Adapt_RoleFields_RoundTripThroughTheEntity()
    {
        var user = NewUser();
        user.IsAdmin = true;
        user.RoleObservedAt = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var config = ScannedConfig();

        var entity = user.Adapt<UserTableEntity>(config);
        var roundTripped = entity.Adapt<User>(config);

        Assert.True(entity.IsAdmin);
        Assert.Equal(user.RoleObservedAt, entity.RoleObservedAt);
        Assert.True(roundTripped.IsAdmin);
        Assert.Equal(user.RoleObservedAt, roundTripped.RoleObservedAt);
    }

    [Fact]
    public void Adapt_RowWithoutRoleColumns_IsNotAdminAndNeverObserved()
    {
        var entity = new UserTableEntity { RowKey = Guid.NewGuid().ToString("D"), Status = "Active" };

        var user = entity.Adapt<User>(ScannedConfig());

        Assert.False(user.IsAdmin);
        Assert.Null(user.RoleObservedAt);
    }

    [Theory]
    [InlineData(UserStatus.Active, "Active")]
    [InlineData(UserStatus.Locked, "Locked")]
    public void Adapt_UserToEntity_StoresStatusAsName(UserStatus status, string expected)
    {
        var entity = NewUser(status).Adapt<UserTableEntity>(ScannedConfig());

        Assert.Equal(expected, entity.Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Adapt_UserToEntity_DoesNotCarryAnETag()
    {
        var entity = NewUser().Adapt<UserTableEntity>(ScannedConfig());

        Assert.Equal(default, entity.ETag);
        Assert.NotEqual(ETag.All, entity.ETag);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Adapt_UserToEntity_NullEmail_StaysNull()
    {
        var user = NewUser();
        user.Email = null;

        var entity = user.Adapt<UserTableEntity>(ScannedConfig());

        Assert.Null(entity.Email);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    [InlineData(" Active")]
    [InlineData("Active ")]
    [InlineData("Admin")]
    [InlineData("1")]
    [InlineData("True")]
    [InlineData("Locked")]
    public void Parse_UnknownOrMissingStatus_ReturnsLocked(string? value)
    {
        Assert.Equal(UserStatus.Locked, UserStatusText.Parse(value));
    }

    [Fact]
    public void Parse_ActiveName_ReturnsActive()
    {
        Assert.Equal(UserStatus.Active, UserStatusText.Parse("Active"));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("garbage")]
    [InlineData("")]
    [InlineData("active")]
    public void Adapt_EntityWithUnknownStatus_MapsToLocked(string status)
    {
        var entity = new UserTableEntity
        {
            PartitionKey = "user",
            RowKey = Guid.NewGuid().ToString("D"),
            Status = status,
        };

        var user = entity.Adapt<User>(ScannedConfig());

        Assert.Equal(UserStatus.Locked, user.Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Adapt_EntityWithNonGuidRowKey_Throws()
    {
        var entity = new UserTableEntity { PartitionKey = "user", RowKey = "not-a-guid", Status = "Active" };
        var config = ScannedConfig();

        Assert.ThrowsAny<Exception>(() => entity.Adapt<User>(config));
    }

    [Fact]
    public void Compile_InfrastructureMappings_Succeeds()
    {
        var config = ScannedConfig();

        var exception = Record.Exception(() => config.Compile());

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UserKeys_RowKey_IsStableLowercaseCanonicalGuid()
    {
        var id = Guid.Parse("ABCDEF01-2345-6789-ABCD-EF0123456789");

        Assert.Equal("abcdef01-2345-6789-abcd-ef0123456789", UserKeys.RowKey(id));
    }
}
