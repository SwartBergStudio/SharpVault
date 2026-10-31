// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Authentication;

public static class TestUsers
{
    private const string UserRole = "SharpVault.User";
    private const string AdminRole = "SharpVault.Admin";

    public static TestUser AuthenticatedWithoutRole { get; } = new(
        Guid.Parse("5f0c1d2e-4b7a-4c3d-9e8f-00000000b001"),
        "Test user without role",
        TestTenant.Id,
        []);

    public static TestUser User { get; } = new(
        Guid.Parse("5f0c1d2e-4b7a-4c3d-9e8f-00000000b002"),
        "Test user",
        TestTenant.Id,
        [UserRole],
        Email: "user@sharpvault.test");

    public static TestUser OtherUser { get; } = new(
        Guid.Parse("5f0c1d2e-4b7a-4c3d-9e8f-00000000b003"),
        "Other test user",
        TestTenant.Id,
        [UserRole],
        Email: "other@sharpvault.test");

    public static TestUser Admin { get; } = new(
        Guid.Parse("5f0c1d2e-4b7a-4c3d-9e8f-00000000b004"),
        "Test admin",
        TestTenant.Id,
        [AdminRole],
        Email: "admin@sharpvault.test");

    public static TestUser LockedUser { get; } = new(
        Guid.Parse("5f0c1d2e-4b7a-4c3d-9e8f-00000000b005"),
        "Locked test user",
        TestTenant.Id,
        [UserRole],
        Email: "locked@sharpvault.test");

    public static TestUser LockedAdmin { get; } = new(
        Guid.Parse("5f0c1d2e-4b7a-4c3d-9e8f-00000000b006"),
        "Locked test admin",
        TestTenant.Id,
        [AdminRole],
        Email: "locked-admin@sharpvault.test");

    public static TestUser Guest { get; } = new(
        Guid.Parse("5f0c1d2e-4b7a-4c3d-9e8f-00000000b007"),
        "Guest test user",
        TestTenant.Id,
        [UserRole],
        IsGuest: true,
        Email: "guest@partner.test");

    public static TestUser OtherTenantUser { get; } = new(
        Guid.Parse("5f0c1d2e-4b7a-4c3d-9e8f-00000000b008"),
        "Other tenant test user",
        TestTenant.OtherId,
        [UserRole],
        Email: "user@other.test");

    public static IReadOnlyList<TestUser> All { get; } =
    [
        AuthenticatedWithoutRole,
        User,
        OtherUser,
        Admin,
        LockedUser,
        LockedAdmin,
        Guest,
        OtherTenantUser,
    ];
}
