// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Users;
using SharpVault.IntegrationTests.ApiTests;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.Storage;

[Collection(nameof(StorageTestGroup))]
public sealed class ListAdminsStoreTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    [Fact]
    public async Task ListAdminsAsync_AgainstAzurite_ReturnsOnlyRowsWithTheFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seededAt = DateTimeOffset.UtcNow;
        await Factory.SeedUserAsync(TestUsers.Admin, UserRows.ActiveStatus, seededAt, cancellationToken);
        await Factory.SeedUserAsync(TestUsers.User, UserRows.ActiveStatus, seededAt, cancellationToken);
        await UserRows.InsertManyAsync(GetUsersTable(), 5, cancellationToken);
        var legacy = new TableEntity(UserRows.PartitionKey, Guid.NewGuid().ToString("D"))
        {
            [UserColumns.DisplayName] = "Legacy",
            [UserColumns.Status] = UserRows.ActiveStatus,
            [UserColumns.CreatedAt] = seededAt,
            [UserColumns.LastSignInAt] = seededAt,
        };
        await GetUsersTable().AddEntityAsync(legacy, cancellationToken);
        var store = new TableUserStore(Factory.StorageAdminClient);

        var admins = await store.ListAdminsAsync(100, cancellationToken);

        var admin = Assert.Single(admins);
        Assert.Equal(TestUsers.Admin.ObjectId, admin.Id);
        Assert.True(admin.IsAdmin);
        Assert.Equal(UserStatus.Active, admin.Status);
    }
}
