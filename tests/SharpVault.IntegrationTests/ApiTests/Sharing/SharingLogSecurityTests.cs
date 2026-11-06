// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Directory;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Logging;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingLogSecurityTests(ApiTestFactory factory) : SharingSecurityTestBase(factory)
{
    private const string FrameworkPrefix = "Microsoft";

    private sealed class FailingGraphHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(body, Encoding.UTF8, "text/plain") });
    }

    private static IEnumerable<string> TextOf(CapturedLogEntry entry) => [entry.Message, entry.Category, entry.Exception ?? string.Empty];

    private void AssertNoLogContains(IEnumerable<string> needles)
    {
        foreach (var needle in needles.Where(value => !string.IsNullOrEmpty(value)))
        {
            var offender = Logs.Entries.FirstOrDefault(entry => TextOf(entry).Any(text => text.Contains(needle, StringComparison.OrdinalIgnoreCase)));
            Assert.True(offender is null, $"A log entry from {offender?.Category} at {offender?.Level} contained a protected value.");
        }
    }

    private async Task<(IReadOnlyList<Guid> Ids, PlaintextMarkers Markers, string Updated, string Term)> RunLifecycleAsync()
    {
        var markers = new PlaintextMarkers();
        var updated = "SV-UPDATED-" + Guid.NewGuid().ToString("N");
        var term = "svsearch" + Guid.NewGuid().ToString("N");
        var folder = await NewFolderAsync(markers.Folder);
        var secret = await NewSecretAsync(markers, folder.Id);
        var owner = SharingFor(Owner);
        var viewerGrant = await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var ownFolder = await NewFolderAsync("recipient-own", owner: TestUsers.ShareViewer);

        await owner.ListSecretGrantsAsync(Owner.ObjectId, secret.Id);
        await owner.SearchDirectoryAsync(term);
        await owner.SearchDirectoryAsync("ab\"" + term);
        await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();
        await SharingFor(TestUsers.ShareViewer).GetSharedSecretAsync(Owner.ObjectId, secret.Id);
        await SharingFor(TestUsers.ShareViewer).RevealSharedSecretAsync(Owner.ObjectId, secret.Id, ViewValue);
        await SharingFor(TestUsers.ShareViewer).RevealSharedSecretAsync(Owner.ObjectId, secret.Id, ViewNotes);
        await SharingFor(TestUsers.ShareViewer).FileSharedSecretAsync(Owner.ObjectId, secret.Id, new FileSharedItemRequestDto { FolderId = ownFolder.Id });
        await SharingFor(TestUsers.FolderViewer).GetSharedFolderAsync(Owner.ObjectId, folder.Id);
        var edited = Require(await SharingFor(TestUsers.ShareEditor).UpdateSharedSecretAsync(
            Owner.ObjectId,
            secret.Id,
            secret.ConcurrencyToken,
            new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = markers.Name, ReplaceValue = true, Value = updated }));
        Require(await owner.ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId, viewerGrant.ConcurrencyToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit }));
        await SharingFor(TestUsers.ShareEditor).RevealSharedSecretAsync(Owner.ObjectId, secret.Id, ViewValue);
        await owner.RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);
        await owner.RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId);
        await owner.RevokeFolderGrantAsync(Owner.ObjectId, folder.Id, TestUsers.FolderViewer.ObjectId);
        await SharingFor(TestUsers.ShareViewer).GetSharedSecretAsync(Owner.ObjectId, secret.Id);
        await SharingFor(TestUsers.OtherUser).GetSharedSecretAsync(Owner.ObjectId, edited.Id);
        return ([folder.Id, secret.Id, ownFolder.Id], markers, updated, term);
    }

    private static IEnumerable<TestUser> Actors => [Owner, TestUsers.ShareViewer, TestUsers.ShareEditor, TestUsers.FolderViewer, TestUsers.OtherUser];

    [Fact]
    public async Task Sharing_Lifecycle_LogsAndAuditContainNoMarkers()
    {
        Logs.Clear();

        var (_, markers, updated, term) = await RunLifecycleAsync();

        Assert.NotEmpty(Logs.Entries);
        var protectedValues = markers.All
            .Append(updated)
            .Append(term)
            .Concat(Actors.Select(user => user.DisplayName))
            .Concat(Actors.Select(user => user.Email ?? string.Empty))
            .Append(Factory.DevelopmentKeyMaterial);
        AssertNoLogContains(protectedValues);

        var auditTexts = new List<string>();
        foreach (var row in await ListAuditRowsAsync())
        {
            auditTexts.Add(string.Join('|', row.Select(pair => pair.Value?.ToString())));
        }

        await foreach (var row in GetTableClient(SharpVault.Infrastructure.Storage.TableNames.SecretAuditLog).QueryAsync<Azure.Data.Tables.TableEntity>(cancellationToken: Token))
        {
            auditTexts.Add(string.Join('|', row.Select(pair => pair.Value?.ToString())));
        }

        Assert.NotEmpty(auditTexts);
        foreach (var text in auditTexts)
        {
            markers.AssertAbsentFrom(text);
            Assert.DoesNotContain(updated, text, StringComparison.Ordinal);
            Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            foreach (var user in Actors)
            {
                Assert.DoesNotContain(user.DisplayName, text, StringComparison.Ordinal);
                Assert.DoesNotContain(user.Email!, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task SharingLifecycle_LogsAtInformationOrAbove_ContainNoIds()
    {
        Logs.Clear();

        var (ids, _, _, _) = await RunLifecycleAsync();

        var ours = Logs.Entries
            .Where(entry => entry.Level >= LogLevel.Information)
            .Where(entry => !entry.Category.StartsWith(FrameworkPrefix, StringComparison.Ordinal) && !entry.Category.Contains("Features.Users", StringComparison.Ordinal))
            .ToList();
        var protectedIds = ids.Concat(Actors.Select(user => user.ObjectId)).ToList();
        foreach (var id in protectedIds)
        {
            Assert.DoesNotContain(ours, entry => TextOf(entry).Any(text =>
                text.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase) || text.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public async Task SearchDirectory_LogsContainNoTerm()
    {
        var term = "svsearch" + Guid.NewGuid().ToString("N");
        Logs.Clear();

        await SharingFor(Owner).SearchDirectoryAsync(term);
        await SharingFor(Owner).SearchDirectoryAsync("x\"" + term);
        await SharingFor(Owner).SearchDirectoryAsync("ab");
        var raw = await ClientFor(Owner).GetAsync($"/api/directory/users?search={term}&other={term}", Token);

        Assert.Equal(HttpStatusCode.OK, raw.StatusCode);
        Assert.NotEmpty(Logs.Entries);
        AssertNoLogContains([term]);
    }

    [Fact]
    public async Task SearchDirectory_GraphFails_Returns500WithNoDetail()
    {
        const string graphBody = "graph-internal-secret-4f1c";
        var term = "svsearch" + Guid.NewGuid().ToString("N");
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient<IUserDirectory, GraphUserDirectory>().ConfigurePrimaryHttpMessageHandler(() => new FailingGraphHandler(graphBody))));
        using var client = CreateClientFor(host, TestTokens.Create(Owner));
        Logs.Clear();

        var response = (await new SharingTestApi(client).SearchDirectoryAsync(term)).Response;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(Token);
        using var document = System.Text.Json.JsonDocument.Parse(text);
        Assert.False(document.RootElement.TryGetProperty("detail", out _));
        Assert.DoesNotContain(graphBody, text, StringComparison.Ordinal);
        Assert.DoesNotContain(term, text, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("graph.microsoft.com", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GraphUserDirectory_Failure_LogsTypeOnly()
    {
        const string graphBody = "graph-internal-secret-4f1c";
        var term = "svsearch" + Guid.NewGuid().ToString("N");
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient<IUserDirectory, GraphUserDirectory>().ConfigurePrimaryHttpMessageHandler(() => new FailingGraphHandler(graphBody))));
        using var client = CreateClientFor(host, TestTokens.Create(Owner));
        Logs.Clear();

        await new SharingTestApi(client).SearchDirectoryAsync(term);

        AssertNoLogContains([graphBody, term]);
        Assert.Contains(Logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Sharing_EveryQuery_HasPartitionKey()
    {
        StorageRequests.Clear();

        await RunLifecycleAsync();

        Assert.NotEmpty(StorageRequests.Requests);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }
}
