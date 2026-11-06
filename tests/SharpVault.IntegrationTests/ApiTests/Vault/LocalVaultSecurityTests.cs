// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using SharpVault.Authentication.Local;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed partial class LocalVaultSecurityTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex("\"traceId\":\"[^\"]*\"", RegexOptions.CultureInvariant)]
    private static partial Regex TraceId();

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(CancellationToken.None);
        factory.StorageRequests.Clear();
        factory.Logs.Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private VaultTestApi ApiFor(LocalAccount account) => new(factory.CreateClientForAccount(account));

    private static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"Arrangement failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }

    private static CreateSecretRequestDto NewSecret(PlaintextMarkers markers, Guid? folderId = null) => new()
    {
        Type = SecretTypeDto.Login,
        Name = markers.Name,
        Username = markers.Username,
        Url = markers.Url,
        Tags = [markers.Tag],
        Value = markers.Value,
        Notes = markers.Notes,
        FolderId = folderId,
    };

    private static async Task<string> NormalisedBodyAsync(HttpResponseMessage response) =>
        TraceId().Replace(await response.Content.ReadAsStringAsync(Token), string.Empty);

    private async Task LockAsync(LocalAccount account)
    {
        var table = factory.StorageAdminClient.GetTableClient(TableNames.Users);
        var row = await UserRows.GetAsync(table, account.Id, Token);
        row![UserColumns.Status] = UserRows.LockedStatus;
        await table.UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, Token);
    }

    [Fact]
    public async Task LocalAdminOne_UserOnesSecret_Returns404IdenticalToRandomId()
    {
        var owner = ApiFor(LocalAccounts.UserOne);
        var markers = new PlaintextMarkers();
        var secret = Require(await owner.CreateSecretAsync(NewSecret(markers)));
        var admin = ApiFor(LocalAccounts.AdminOne);
        Require(await admin.CreateSecretAsync(NewSecret(new PlaintextMarkers())));

        var foreign = await admin.GetSecretAsync(secret.Id);
        var random = await admin.GetSecretAsync(Guid.NewGuid());
        var foreignReveal = await admin.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        var randomReveal = await admin.RevealSecretAsync(Guid.NewGuid(), new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.NotFound, foreign.Response.StatusCode);
        Assert.Equal(random.Response.StatusCode, foreign.Response.StatusCode);
        Assert.Equal(await NormalisedBodyAsync(random.Response), await NormalisedBodyAsync(foreign.Response));
        Assert.Equal(HttpStatusCode.NotFound, foreignReveal.Response.StatusCode);
        Assert.Equal(await NormalisedBodyAsync(randomReveal.Response), await NormalisedBodyAsync(foreignReveal.Response));
        markers.AssertAbsentFrom(await foreignReveal.Response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task LocalUserTwo_UserOnesSecret_Returns404()
    {
        var owner = ApiFor(LocalAccounts.UserOne);
        var scenario = await VaultScenario.ArrangeAsync(owner);
        var other = ApiFor(LocalAccounts.UserTwo);
        Require(await other.CreateSecretAsync(NewSecret(new PlaintextMarkers())));

        foreach (var endpointId in VaultEndpointCalls.NonOwnerNotFoundIds)
        {
            var response = await VaultEndpointCalls.SendAsync(endpointId, other, scenario);

            Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"{endpointId} answered {response.StatusCode}.");
            scenario.Markers.AssertAbsentFrom(await response.Content.ReadAsStringAsync(Token));
        }

        Assert.Equal(scenario.Markers.Value + "-v2", (await owner.RevealSecretAsync(scenario.SecretS, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View })).Body!.Value);
    }

    [Fact]
    public async Task LocalAdminOne_Lists_ContainNoneOfUserOnesIds()
    {
        var scenario = await VaultScenario.ArrangeAsync(ApiFor(LocalAccounts.UserOne));
        var admin = ApiFor(LocalAccounts.AdminOne);
        Require(await admin.CreateSecretAsync(NewSecret(new PlaintextMarkers())));

        foreach (var endpointId in new[] { "E1", "E11", "E16" })
        {
            var response = await VaultEndpointCalls.SendAsync(endpointId, admin, scenario);
            var body = await response.Content.ReadAsStringAsync(Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            scenario.Ids.ToList().ForEach(id => Assert.DoesNotContain(id.ToString("D"), body, StringComparison.OrdinalIgnoreCase));
            scenario.Markers.AssertAbsentFrom(body);
        }
    }

    [Fact]
    public async Task LocalDeniedCallers_MakeNoVaultStorageCalls()
    {
        var scenario = await VaultScenario.ArrangeAsync(ApiFor(LocalAccounts.UserOne));
        var denied = new List<HttpClient>
        {
            factory.CreateUnauthenticatedClient(),
            factory.CreateClientWithToken(LocalTokens.AlgNone(LocalAccounts.UserOne)),
        };
        await LockAsync(LocalAccounts.UserTwo);
        denied.Add(factory.CreateClientForAccount(LocalAccounts.UserTwo));
        await LockAsync(LocalAccounts.UserOne);
        denied.Add(factory.CreateClientForAccount(LocalAccounts.UserOne));
        factory.StorageRequests.Clear();

        foreach (var client in denied)
        {
            foreach (var endpointId in VaultEndpointCalls.AllIds)
            {
                var response = await VaultEndpointCalls.SendAsync(endpointId, new VaultTestApi(client), scenario);

                Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"{endpointId} answered {response.StatusCode}.");
            }
        }

        Assert.Empty(factory.StorageRequests.VaultRequests());
        denied.ForEach(client => client.Dispose());
    }

    [Theory]
    [InlineData("{\"type\":\"Login\",\"name\":\"n\",\"unknown\":1}")]
    [InlineData("{\"type\":1,\"name\":\"n\"}")]
    [InlineData("{\"type\":\"Nope\",\"name\":\"n\"}")]
    [InlineData("{\"type\":\"Login\",\"name\":\"\\ud800\"}")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{")]
    public async Task LocalMode_MalformedBody_Returns400(string json)
    {
        var api = ApiFor(LocalAccounts.UserOne);

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
    }

    [Fact]
    public async Task LocalHost_RestartedWithSameKeyMaterial_RevealReturnsStoredValue()
    {
        var markers = new PlaintextMarkers();
        var original = ApiFor(LocalAccounts.UserOne);
        var folder = Require(await original.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var secret = Require(await original.CreateSecretAsync(NewSecret(markers, folder.Id)));
        using var restarted = factory.WithWebHostBuilder(builder =>
            builder.UseSetting($"{EncryptionOptions.SectionName}:{nameof(EncryptionOptions.DevelopmentKeyMaterial)}", factory.DevelopmentKeyMaterial));
        using var client = LocalApiTestFactory.CreateClientFor(restarted, LocalTokens.For(restarted.Services, LocalAccounts.UserOne));
        var api = new VaultTestApi(client);

        var reveal = await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        var read = await api.GetSecretAsync(secret.Id);
        var folders = await api.ListFoldersAsync();

        Assert.Equal(HttpStatusCode.OK, reveal.Response.StatusCode);
        Assert.Equal(markers.Value, reveal.Body!.Value);
        Assert.False(read.Body!.IsDamaged);
        Assert.Equal(markers.Name, read.Body.Name);
        Assert.Equal(markers.Folder, Assert.Single(folders.Body!.Items).Name);
    }
}
