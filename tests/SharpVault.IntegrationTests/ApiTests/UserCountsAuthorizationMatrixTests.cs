// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Azure.Data.Tables;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.IntegrationTests.ApiTests.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class UserCountsAuthorizationMatrixTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private const string Anonymous = "Anonymous";
    private const string AlgNone = "AlgNone";
    private const string UnknownKey = "UnknownKey";
    private const string OtherTenant = "OtherTenant";
    private const string Guest = "Guest";
    private const string LockedAdmin = "LockedAdmin";
    private const string NoRole = "NoRole";
    private const string User = "User";
    private const string ShareViewer = "ShareViewer";
    private const string ShareEditor = "ShareEditor";
    private const string ShareManager = "ShareManager";
    private const string OtherUser = "OtherUser";
    private const string DemotedAdmin = "DemotedAdmin";
    private const string Admin = "Admin";
    private const string OwnerTarget = "Owner";
    private const string SelfTarget = "Self";
    private const string UnknownTarget = "Unknown";
    private const int KeyBytes = 32;
    private const string IsAdminColumn = UserColumns.IsAdmin;
    private const string RoleObservedAtColumn = UserColumns.RoleObservedAt;
    private const int DemotionObservedMinutes = 1;

    public static TheoryData<string, string, int> Matrix() =>
        new()
        {
            { Anonymous, OwnerTarget, 401 },
            { AlgNone, OwnerTarget, 401 },
            { UnknownKey, OwnerTarget, 401 },
            { OtherTenant, OwnerTarget, 401 },
            { Guest, OwnerTarget, 403 },
            { LockedAdmin, OwnerTarget, 403 },
            { NoRole, OwnerTarget, 403 },
            { User, SelfTarget, 403 },
            { User, OwnerTarget, 403 },
            { ShareViewer, OwnerTarget, 403 },
            { ShareEditor, OwnerTarget, 403 },
            { ShareManager, OwnerTarget, 403 },
            { OtherUser, OwnerTarget, 403 },
            { DemotedAdmin, OwnerTarget, 403 },
            { Admin, OwnerTarget, 200 },
            { Admin, SelfTarget, 200 },
            { Admin, UnknownTarget, 404 },
        };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task UserCounts_AuthorizationMatrix_ReturnsExpectedStatus(string caller, string target, int expected)
    {
        await SeedOwnerAndRecipientsAsync();
        using var client = await ClientForCallerAsync(caller);
        var targetId = TargetOf(target, caller);

        var response = await client.GetAsync(CountsPath(targetId), Token);

        Assert.Equal(expected, (int)response.StatusCode);
    }

    private async Task SeedOwnerAndRecipientsAsync()
    {
        foreach (var user in new[] { TestUsers.Admin, TestUsers.User, TestUsers.OtherUser, TestUsers.ShareViewer, TestUsers.ShareEditor, TestUsers.ShareManager, TestUsers.LockedAdmin, TestUsers.ThirdAdmin })
        {
            await WarmUpAsync(user);
        }
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
    }

    private async Task<HttpClient> ClientForCallerAsync(string caller)
    {
        switch (caller)
        {
            case Anonymous:
                return Factory.CreateUnauthenticatedClient();
            case AlgNone:
                return Factory.CreateClientWithToken(TamperedTokens.AlgNone(TestUsers.Admin));
            case UnknownKey:
                return Factory.CreateClientWithToken(TamperedTokens.HmacSignedWithKeyMaterial(TestUsers.Admin, RandomNumberGenerator.GetBytes(KeyBytes)));
            case OtherTenant:
                return Factory.CreateAuthenticatedClient(TestUsers.OtherTenantUser);
            case Guest:
                return Factory.CreateAuthenticatedClient(TestUsers.Guest);
            case LockedAdmin:
                await LockUserAsync(TestUsers.LockedAdmin.ObjectId);
                return Factory.CreateAuthenticatedClient(TestUsers.LockedAdmin);
            case NoRole:
                return Factory.CreateAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);
            case ShareViewer:
                return Factory.CreateAuthenticatedClient(TestUsers.ShareViewer);
            case ShareEditor:
                return Factory.CreateAuthenticatedClient(TestUsers.ShareEditor);
            case ShareManager:
                return Factory.CreateAuthenticatedClient(TestUsers.ShareManager);
            case OtherUser:
                return Factory.CreateAuthenticatedClient(TestUsers.OtherUser);
            case DemotedAdmin:
                await DemoteAsync(TestUsers.ThirdAdmin.ObjectId);
                return Factory.CreateAuthenticatedClient(TestUsers.ThirdAdmin);
            case Admin:
                return Factory.CreateAuthenticatedClient(TestUsers.Admin);
            default:
                return Factory.CreateAuthenticatedClient(TestUsers.User);
        }
    }

    private static Guid TargetOf(string target, string caller) => target switch
    {
        SelfTarget => CallerId(caller),
        UnknownTarget => Guid.NewGuid(),
        _ => OwnerId,
    };

    private static Guid CallerId(string caller) => caller switch
    {
        Admin => TestUsers.Admin.ObjectId,
        _ => TestUsers.User.ObjectId,
    };

    private async Task DemoteAsync(Guid oid)
    {
        var row = await RequireUserRowAsync(oid);
        row[IsAdminColumn] = false;
        row[RoleObservedAtColumn] = DateTimeOffset.UtcNow.AddMinutes(DemotionObservedMinutes);
        await GetUsersTable().UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, Token);
    }

    private static string CountsPath(Guid userId) => ApiRoutes.AdminUserCounts.Replace("{userId:guid}", userId.ToString("D"), StringComparison.Ordinal);
}
