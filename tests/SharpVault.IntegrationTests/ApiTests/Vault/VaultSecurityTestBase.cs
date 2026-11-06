// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

public abstract partial class VaultSecurityTestBase(ApiTestFactory factory) : VaultTestBase(factory)
{
    protected static readonly RevealSecretRequestDto ViewValue = new() { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };

    protected static readonly RevealSecretRequestDto ViewNotes = new() { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.View };

    protected static CreateSecretRequestDto NewSecretRequest(PlaintextMarkers markers, Guid? folderId = null) => new()
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

    protected static UpdateSecretRequestDto UpdateRequest(PlaintextMarkers markers, string? newValue = null) => new()
    {
        Type = SecretTypeDto.Login,
        Name = markers.Name,
        Username = markers.Username,
        Url = markers.Url,
        Tags = [markers.Tag],
        ReplaceValue = newValue is not null,
        Value = newValue,
    };

    protected static async Task<string> BodyOfAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync(Token);

    protected static async Task<string> BodyWithoutTraceAsync(HttpResponseMessage response) =>
        TraceId().Replace(await BodyOfAsync(response), string.Empty);

    protected static async Task<JsonElement> ProblemOfAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await BodyOfAsync(response));
        return document.RootElement.Clone();
    }

    protected static async Task<string> RevealValueAsync(VaultTestApi api, Guid secretId)
    {
        var result = await api.RevealSecretAsync(secretId, ViewValue);
        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        return result.Body!.Value;
    }

    protected async Task<IReadOnlyList<string>> RowsOfAsync(Guid owner)
    {
        var rows = await VaultRows.ListPartitionAsync(VaultsTable, owner, Token);
        return [.. rows.Select(row => $"{row.RowKey}|{row.ETag}").Order(StringComparer.Ordinal)];
    }

    protected async Task<int> TotalAuditRowsAsync()
    {
        var count = (await ListAuditRowsAsync()).Count;
        await foreach (var _ in GetTableClient(TableNames.SecretAuditLog).QueryAsync<TableEntity>(select: ["PartitionKey"], cancellationToken: Token))
        {
            count++;
        }

        return count;
    }

    protected static async Task<IReadOnlyList<Guid>> RunLifecycleAsync(VaultTestApi api, PlaintextMarkers markers)
    {
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var secret = await CreateSecretAsync(api, markers, folder.Id);
        var updated = Require(await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(markers, markers.Value + "-v2")));
        await RevealValueAsync(api, secret.Id);
        Require(await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.Copy }));
        Require(await api.RevealSecretAsync(secret.Id, ViewNotes));
        Require(await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.Copy }));
        var version = Require(await api.ListHistoryAsync(secret.Id)).Items[0].VersionId;
        Require(await api.RevealVersionAsync(secret.Id, version, new RevealVersionRequestDto { Intent = RevealIntentDto.Copy }));
        var restored = Require(await api.RestoreVersionAsync(secret.Id, version, updated.ConcurrencyToken));
        var moved = Require(await api.MoveSecretAsync(secret.Id, restored.ConcurrencyToken, new MoveSecretRequestDto { FolderId = null }));
        Require(await api.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = markers.Folder + "-renamed" }));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(secret.Id, moved.ConcurrencyToken)).StatusCode);
        Require(await api.RestoreRecycledSecretAsync(secret.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        Require(await api.RestoreRecycledFolderAsync(folder.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PurgeRecycledFolderAsync(folder.Id)).StatusCode);
        var current = Require(await api.GetSecretAsync(secret.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(secret.Id, current.ConcurrencyToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.PurgeRecycledSecretAsync(secret.Id)).StatusCode);
        return [folder.Id, secret.Id, version];
    }

    protected WebApplicationFactory<Program> WithThrowingAudit() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLog>();
            services.AddSingleton<IAuditLog, ThrowingAuditLog>();
        }));

    protected static VaultTestApi ApiOn(WebApplicationFactory<Program> host, TestUser user) =>
        new(CreateClientFor(host, TestTokens.Create(user)));

    [GeneratedRegex("\"traceId\":\"[^\"]*\"", RegexOptions.CultureInvariant)]
    private static partial Regex TraceId();
}
