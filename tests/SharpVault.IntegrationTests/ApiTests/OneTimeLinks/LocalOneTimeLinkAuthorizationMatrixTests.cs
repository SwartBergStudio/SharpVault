// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Azure.Data.Tables;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalOneTimeLinkAuthorizationMatrixTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const string Settings = "E1";
    private const string Create = "E3";
    private const string Revoke = "E5";
    private const string Dismiss = "E7";
    private const string OpenAgent = "SharpVault-Test-Browser/1.0";

    private static readonly string[] EndpointIds = [Settings, Create, Revoke, Dismiss];

    private static readonly string[] AnonymousFamily = ["Anonymous", "UnknownKey"];

    private static readonly string[] ForbiddenFamily = ["LockedUser"];

    private static readonly string[] ManagerFamily = ["Owner", "Manager"];

    private static readonly string[] ReaderFamily = ["Viewer", "AdminOne"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, HttpStatusCode> MatrixCases()
    {
        var cases = new TheoryData<string, string, HttpStatusCode>();
        foreach (var caller in AnonymousFamily.Concat(ForbiddenFamily).Concat(ManagerFamily).Concat(ReaderFamily))
        {
            foreach (var endpoint in EndpointIds)
            {
                cases.Add(endpoint, caller, ExpectedStatus(endpoint, caller));
            }
        }

        return cases;
    }

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(Token);
        factory.StorageRequests.Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task LocalOneTimeLinkAuthorizationMatrix_EveryEndpointAndCaller_ReturnsExpectedStatus(string endpoint, string caller, HttpStatusCode expected)
    {
        var scenario = await ArrangeAsync(caller);
        using var client = ClientFor(caller);

        using var response = await SendAsync(client, endpoint, caller, scenario);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task LocalOneTimeLinkAuthorizationMatrix_EveryEndpoint_NoQueryWithoutPartitionKey()
    {
        var scenario = await ArrangeAsync("Manager");
        factory.StorageRequests.Clear();
        var callers = new[] { "Owner", "Manager", "Viewer", "AdminOne" };

        foreach (var caller in callers)
        {
            using var client = ClientFor(caller);
            foreach (var endpoint in EndpointIds)
            {
                using var response = await SendAsync(client, endpoint, caller, scenario);
            }
        }

        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
    }

    private static HttpStatusCode ExpectedStatus(string endpoint, string caller)
    {
        if (AnonymousFamily.Contains(caller))
        {
            return HttpStatusCode.Unauthorized;
        }

        if (ForbiddenFamily.Contains(caller))
        {
            return HttpStatusCode.Forbidden;
        }

        if (ManagerFamily.Contains(caller))
        {
            return endpoint switch
            {
                Settings => HttpStatusCode.OK,
                Create => HttpStatusCode.Created,
                _ => HttpStatusCode.NoContent,
            };
        }

        return endpoint == Settings ? HttpStatusCode.OK : HttpStatusCode.NotFound;
    }

    private static SharePermissionDto PermissionFor(string caller) => caller switch
    {
        "Viewer" => SharePermissionDto.View,
        _ => SharePermissionDto.Manage,
    };

    private HttpClient ClientFor(string caller)
    {
        var wrongKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength));
        return caller switch
        {
            "Anonymous" => factory.CreateUnauthenticatedClient(),
            "UnknownKey" => factory.CreateClientWithToken(LocalTokens.Builder(LocalAccounts.AdminOne, wrongKey).Build()),
            "Owner" => factory.CreateClientForAccount(LocalAccounts.UserOne),
            "Manager" or "Viewer" or "LockedUser" => factory.CreateClientForAccount(LocalAccounts.UserTwo),
            "AdminOne" => factory.CreateClientForAccount(LocalAccounts.AdminOne),
            _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, null),
        };
    }

    private async Task<Scenario> ArrangeAsync(string caller)
    {
        var permission = PermissionFor(caller);
        using var owner = factory.CreateClientForAccount(LocalAccounts.UserOne);
        var vault = new VaultTestApi(owner);
        var sharing = new SharingTestApi(owner);
        var markers = new PlaintextMarkers();
        var secret = Require(await vault.CreateSecretAsync(NewSecret(markers)));
        Require(await sharing.CreateSecretGrantAsync(
            LocalAccounts.UserOne.Id,
            secret.Id,
            new CreateShareGrantRequestDto { RecipientId = LocalAccounts.UserTwo.Id, Permission = permission }));

        var ownerLink = await CreateLinkAsync(LocalAccounts.UserOne, secret.Id);
        var ownerNoticeLink = await CreateLinkAsync(LocalAccounts.UserOne, secret.Id);
        await OpenAnonymouslyAsync(ownerNoticeLink);
        var ownerNotice = Assert.Single((await NoticesAsync(LocalAccounts.UserOne)).Items, notice => notice.LinkId == ownerNoticeLink);

        var managerLink = Guid.Empty;
        var managerNotice = string.Empty;
        if (permission == SharePermissionDto.Manage)
        {
            managerLink = await CreateLinkAsync(LocalAccounts.UserTwo, secret.Id);
            var managerNoticeLink = await CreateLinkAsync(LocalAccounts.UserTwo, secret.Id);
            await OpenAnonymouslyAsync(managerNoticeLink);
            managerNotice = Assert.Single((await NoticesAsync(LocalAccounts.UserTwo)).Items, notice => notice.LinkId == managerNoticeLink).NoticeId;
        }

        if (caller == "LockedUser")
        {
            await LockAsync(LocalAccounts.UserTwo.Id);
        }

        return new Scenario(secret.Id, ownerLink, managerLink, ownerNotice.NoticeId, managerNotice);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string endpoint, string caller, Scenario scenario)
    {
        var isManager = caller == "Manager";
        return endpoint switch
        {
            Settings => await client.GetAsync(ApiRoutes.OneTimeLinkSettings, Token),
            Create => await client.PostAsJsonAsync(
                SecretRoute(ApiRoutes.OneTimeLinkSecret, LocalAccounts.UserOne.Id, scenario.SecretId),
                new CreateOneTimeLinkRequestDto { Payload = NewPayload() },
                Token),
            Revoke => await client.DeleteAsync(LinkRoute(isManager ? scenario.ManagerLinkId : scenario.OwnerLinkId), Token),
            Dismiss => await client.DeleteAsync(NoticeRoute(isManager ? scenario.ManagerNoticeId : scenario.OwnerNoticeId), Token),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
        };
    }

    private async Task<Guid> CreateLinkAsync(LocalAccount creator, Guid secretId)
    {
        using var client = factory.CreateClientForAccount(creator);
        using var response = await client.PostAsJsonAsync(
            SecretRoute(ApiRoutes.OneTimeLinkSecret, LocalAccounts.UserOne.Id, secretId),
            new CreateOneTimeLinkRequestDto { Payload = NewPayload() },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkDto>(Token);
        Assert.NotNull(body);
        return body.Id;
    }

    private async Task OpenAnonymouslyAsync(Guid linkId)
    {
        var path = ApiRoutes.OneTimeLinkOpen.Replace("{linkId:guid}", linkId.ToString("D"), StringComparison.Ordinal);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent);
        using var client = factory.CreateUnauthenticatedClient();
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<OneTimeLinkNoticeListDto> NoticesAsync(LocalAccount account)
    {
        using var client = factory.CreateClientForAccount(account);
        using var response = await client.GetAsync(ApiRoutes.OneTimeLinkNotices, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OneTimeLinkNoticeListDto>(Token))!;
    }

    private async Task LockAsync(Guid id)
    {
        var table = factory.StorageAdminClient.GetTableClient(TableNames.Users);
        var row = await UserRows.GetAsync(table, id, Token);
        row![UserColumns.Status] = UserRows.LockedStatus;
        await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, Token);
    }

    private static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"Arrangement failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }

    private static CreateSecretRequestDto NewSecret(PlaintextMarkers markers) => new()
    {
        Type = SecretTypeDto.Login,
        Name = markers.Name,
        Username = markers.Username,
        Url = markers.Url,
        Tags = [markers.Tag],
        Value = markers.Value,
        Notes = markers.Notes,
    };

    private static string SecretRoute(string template, Guid ownerId, Guid secretId) =>
        template
            .Replace("{ownerId:guid}", ownerId.ToString("D"), StringComparison.Ordinal)
            .Replace("{secretId:guid}", secretId.ToString("D"), StringComparison.Ordinal);

    private static string LinkRoute(Guid linkId) =>
        ApiRoutes.OneTimeLink.Replace("{linkId:guid}", linkId.ToString("D"), StringComparison.Ordinal);

    private static string NoticeRoute(string noticeId) =>
        ApiRoutes.OneTimeLinkNotice.Replace("{noticeId}", Uri.EscapeDataString(noticeId), StringComparison.Ordinal);

    private static string NewPayload()
    {
        var marker = "PLAINTEXT-" + Guid.NewGuid().ToString("N");
        var bytes = new byte[1 + marker.Length];
        bytes[0] = 0x80;
        System.Text.Encoding.ASCII.GetBytes(marker, bytes.AsSpan(1));
        return Convert.ToBase64String(bytes);
    }

    private sealed record Scenario(Guid SecretId, Guid OwnerLinkId, Guid ManagerLinkId, string OwnerNoticeId, string ManagerNoticeId);
}
