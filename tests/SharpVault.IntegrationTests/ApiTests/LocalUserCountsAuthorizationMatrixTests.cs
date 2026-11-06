// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Cryptography;
using Azure.Data.Tables;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalUserCountsAuthorizationMatrixTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const string Anonymous = "Anonymous";
    private const string AlgNone = "AlgNone";
    private const string UnknownKey = "UnknownKey";
    private const string WrongTenant = "WrongTenant";
    private const string Expired = "Expired";
    private const string ForgedAdmin = "ForgedAdmin";
    private const string Guest = "Guest";
    private const string NoRole = "NoRole";
    private const string UserOne = "UserOne";
    private const string UserTwo = "UserTwo";
    private const string LockedUser = "LockedUser";
    private const string LockedAdmin = "LockedAdmin";
    private const string DemotedAdmin = "DemotedAdmin";
    private const string AdminOne = "AdminOne";
    private const string OwnerTarget = "Owner";
    private const string SelfTarget = "Self";
    private const string UnknownTarget = "Unknown";
    private const int DemotionObservedMinutes = 1;

    public static TheoryData<string, string, int> Matrix() =>
        new()
        {
            { Anonymous, OwnerTarget, 401 },
            { AlgNone, OwnerTarget, 401 },
            { UnknownKey, OwnerTarget, 401 },
            { WrongTenant, OwnerTarget, 401 },
            { Expired, OwnerTarget, 401 },
            { ForgedAdmin, OwnerTarget, 401 },
            { Guest, OwnerTarget, 403 },
            { NoRole, OwnerTarget, 403 },
            { UserOne, SelfTarget, 403 },
            { UserTwo, OwnerTarget, 403 },
            { LockedUser, OwnerTarget, 403 },
            { LockedAdmin, OwnerTarget, 403 },
            { DemotedAdmin, OwnerTarget, 403 },
            { AdminOne, OwnerTarget, 200 },
            { AdminOne, SelfTarget, 200 },
            { AdminOne, UnknownTarget, 404 },
        };

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(CancellationToken.None);
        factory.StorageRequests.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
        return ValueTask.CompletedTask;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task LocalUserCounts_AuthorizationMatrix_ReturnsExpectedStatus(string caller, string target, int expected)
    {
        await SeedOwnerAndRecipientAsync();
        using var client = await ClientForCallerAsync(caller);

        var response = await client.GetAsync(CountsPath(TargetOf(target, caller)), Token);

        Assert.Equal(expected, (int)response.StatusCode);
        Assert.DoesNotContain("SV-", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
    }

    private async Task SeedOwnerAndRecipientAsync()
    {
        using var owner = factory.CreateClientForAccount(LocalAccounts.UserOne);
        var (created, secret) = await new VaultTestApi(owner).CreateSecretAsync(SharingScenario.NewSecret(new PlaintextMarkers(), null));
        Assert.True(created.IsSuccessStatusCode, $"Creating the secret failed with {created.StatusCode}.");
        var secretId = secret?.Id ?? throw new InvalidOperationException("The create response had no body.");
        var grant = await new SharingTestApi(owner).CreateSecretGrantAsync(
            LocalAccounts.UserOne.Id,
            secretId,
            new CreateShareGrantRequestDto { RecipientId = LocalAccounts.UserTwo.Id, Permission = SharePermissionDto.View });
        Assert.True(grant.Response.IsSuccessStatusCode, $"Sharing failed with {grant.Response.StatusCode}.");
    }

    private async Task<HttpClient> ClientForCallerAsync(string caller)
    {
        switch (caller)
        {
            case Anonymous:
                return factory.CreateUnauthenticatedClient();
            case AlgNone:
                return factory.CreateClientWithToken(LocalTokens.AlgNone(LocalAccounts.AdminOne));
            case UnknownKey:
                return factory.CreateClientWithToken(ForgedTokens.SignedWithUnknownKey());
            case WrongTenant:
                return factory.CreateClientWithToken(Builder(LocalAccounts.AdminOne).WithClaim(EntraClaimTypes.TenantId, Guid.NewGuid().ToString("D")).Build());
            case Expired:
                return factory.CreateClientWithToken(Builder(LocalAccounts.AdminOne)
                    .NotBefore(DateTimeOffset.UtcNow.AddMinutes(-10))
                    .ExpiresAt(DateTimeOffset.UtcNow.AddMinutes(-2))
                    .Build());
            case ForgedAdmin:
                var wrongKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength));
                return factory.CreateClientWithToken(LocalTokens.Builder(LocalAccounts.AdminOne, wrongKey).Build());
            case Guest:
                return factory.CreateClientWithToken(Builder(LocalAccounts.AdminOne).WithClaim(EntraClaimTypes.AccountType, EntraClaimTypes.GuestAccountType).Build());
            case NoRole:
                return factory.CreateClientWithToken(Builder(LocalAccounts.UserOne).WithoutClaim(EntraClaimTypes.Roles).Build());
            case UserOne:
                return factory.CreateClientForAccount(LocalAccounts.UserOne);
            case UserTwo:
                return factory.CreateClientForAccount(LocalAccounts.UserTwo);
            case LockedUser:
                await SetStatusAsync(LocalAccounts.UserTwo.Id, UserRows.LockedStatus);
                return factory.CreateClientForAccount(LocalAccounts.UserTwo);
            case LockedAdmin:
                await SetStatusAsync(LocalAccounts.AdminTwo.Id, UserRows.LockedStatus);
                return factory.CreateClientForAccount(LocalAccounts.AdminTwo);
            case DemotedAdmin:
                await DemoteAsync(LocalAccounts.AdminTwo.Id);
                return factory.CreateClientForAccount(LocalAccounts.AdminTwo);
            default:
                return factory.CreateClientForAccount(LocalAccounts.AdminOne);
        }
    }

    private TestTokenBuilder Builder(LocalAccount account) => LocalTokens.Builder(factory.Services, account);

    private static Guid TargetOf(string target, string caller) => target switch
    {
        SelfTarget => CallerId(caller),
        UnknownTarget => Guid.NewGuid(),
        _ => LocalAccounts.UserOne.Id,
    };

    private static Guid CallerId(string caller) => caller switch
    {
        AdminOne => LocalAccounts.AdminOne.Id,
        _ => LocalAccounts.UserOne.Id,
    };

    private async Task SetStatusAsync(Guid id, string status)
    {
        var table = factory.StorageAdminClient.GetTableClient(TableNames.Users);
        var row = await UserRows.GetAsync(table, id, Token) ?? throw new InvalidOperationException("The user row is missing.");
        row[UserColumns.Status] = status;
        await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, Token);
    }

    private async Task DemoteAsync(Guid id)
    {
        var table = factory.StorageAdminClient.GetTableClient(TableNames.Users);
        var row = await UserRows.GetAsync(table, id, Token) ?? throw new InvalidOperationException("The user row is missing.");
        row[UserColumns.IsAdmin] = false;
        row[UserColumns.RoleObservedAt] = DateTimeOffset.UtcNow.AddMinutes(DemotionObservedMinutes);
        await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, Token);
    }

    private static string CountsPath(Guid userId) => ApiRoutes.AdminUserCounts.Replace("{userId:guid}", userId.ToString("D"), StringComparison.Ordinal);
}
