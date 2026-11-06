// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpVault.Authentication.Local;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Encryption;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalVaultKeyChangeTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const string DecryptionTitle = "The item could not be read.";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(CancellationToken.None);
        factory.Logs.Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"Arrangement failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }

    private static CreateSecretRequestDto NewSecret(PlaintextMarkers markers, Guid? folderId) => new()
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

    [Fact]
    public async Task LocalHost_RestartedWithOtherKeyMaterial_ListShowsDamagedAndRevealReturnsGeneric500()
    {
        var markers = new PlaintextMarkers();
        using var original = LocalApiTestFactory.CreateClientFor(factory, LocalTokens.For(factory.Services, LocalAccounts.UserOne));
        var originalApi = new VaultTestApi(original);
        var folder = Require(await originalApi.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var secret = Require(await originalApi.CreateSecretAsync(NewSecret(markers, folder.Id)));
        var recycled = Require(await originalApi.CreateSecretAsync(NewSecret(new PlaintextMarkers(), null)));
        Assert.Equal(HttpStatusCode.NoContent, (await originalApi.DeleteSecretAsync(recycled.Id, recycled.ConcurrencyToken)).StatusCode);
        factory.Logs.Clear();
        var otherMaterial = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        using var restarted = factory.WithWebHostBuilder(builder =>
            builder.UseSetting($"{EncryptionOptions.SectionName}:{nameof(EncryptionOptions.DevelopmentKeyMaterial)}", otherMaterial));
        using var client = LocalApiTestFactory.CreateClientFor(restarted, LocalTokens.For(restarted.Services, LocalAccounts.UserOne));
        var api = new VaultTestApi(client);

        var secrets = await api.ListSecretsAsync(pageSize: 50);
        var folders = await api.ListFoldersAsync();
        var bin = await api.ListRecycleBinAsync();
        var single = await api.GetSecretAsync(secret.Id);
        var reveal = await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        var create = await api.CreateSecretAsync(NewSecret(new PlaintextMarkers(), null));

        Assert.Equal(HttpStatusCode.OK, secrets.Response.StatusCode);
        var listed = Assert.Single(secrets.Body!.Items);
        Assert.True(listed.IsDamaged);
        Assert.Null(listed.Name);
        Assert.Null(listed.Username);
        Assert.Null(listed.Url);
        Assert.Equal(HttpStatusCode.OK, folders.Response.StatusCode);
        Assert.True(Assert.Single(folders.Body!.Items).IsDamaged);
        Assert.Equal(HttpStatusCode.OK, bin.Response.StatusCode);
        Assert.True(Assert.Single(bin.Body!.Items).IsDamaged);
        Assert.Equal(HttpStatusCode.OK, single.Response.StatusCode);
        Assert.True(single.Body!.IsDamaged);
        Assert.Equal(HttpStatusCode.InternalServerError, reveal.Response.StatusCode);
        Assert.Equal(DecryptionTitle, JsonDocument.Parse(await reveal.Response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.InternalServerError, create.Response.StatusCode);
        Assert.Contains(factory.Logs.Entries, entry => entry.Level == LogLevel.Error && entry.Category.EndsWith("VaultKeyRingService", StringComparison.Ordinal));
        Assert.False(factory.Logs.ContainsText(otherMaterial));
        Assert.False(factory.Logs.ContainsText(factory.DevelopmentKeyMaterial));
        Assert.False(factory.Logs.ContainsText(markers.Value));
    }
}
