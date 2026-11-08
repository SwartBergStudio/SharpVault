// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.ApiTests.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingAuthorizationMatrixTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private const string RevealEndpointPrefix = "E24";

    public static TheoryData<string> EndpointIds() => [.. SharingMatrixEndpoints.All.Select(endpoint => endpoint.Id)];

    public static TheoryData<string> RepresentativeEndpointIds() => ["E21", "E22-S", "E25-G", "E30-S-grantee", "E33-F"];

    public static TheoryData<string, CallerKind, HttpStatusCode> MatrixCases()
    {
        var cases = new TheoryData<string, CallerKind, HttpStatusCode>();
        foreach (var endpoint in SharingMatrixEndpoints.All)
        {
            foreach (var caller in SharingMatrixEndpoints.DeniedCallers)
            {
                cases.Add(endpoint.Id, caller, SharingMatrixEndpoints.ExpectedDenied(caller));
            }

            foreach (var caller in SharingMatrixEndpoints.ShapeCallers)
            {
                cases.Add(endpoint.Id, caller, endpoint.ExpectedFor(caller));
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task SharingAuthorizationMatrix_EveryEndpointAndCaller_ReturnsExpectedStatus(string endpointId, CallerKind caller, HttpStatusCode expected)
    {
        var scenario = await ArrangeAsync();
        using var client = await ArrangeCallerAsync(caller);

        var response = await SharingMatrixEndpoints.Find(endpointId).Send(new SharingTestApi(client), scenario);

        Assert.Equal(expected, response.StatusCode);
        await AssertBodyRevealsNothingAsync(response, scenario, endpointId);
    }

    [Theory]
    [MemberData(nameof(RepresentativeEndpointIds))]
    public async Task SharingAuthorizationMatrix_DeniedCallers_MakeNoVaultInboxOrKeyVaultCalls(string endpointId)
    {
        var scenario = await ArrangeAsync();
        var endpoint = SharingMatrixEndpoints.Find(endpointId);
        var clients = new List<HttpClient>();
        foreach (var caller in SharingMatrixEndpoints.DeniedCallers)
        {
            clients.Add(await ArrangeCallerAsync(caller));
        }

        await LockUserAsync(TestUsers.User.ObjectId);
        clients.Add(ClientFor(TestUsers.User));
        ResetCounters();
        Factory.GraphDirectory.Clear();

        foreach (var client in clients)
        {
            var response = await endpoint.Send(new SharingTestApi(client), scenario);
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"Unexpected status {response.StatusCode}.");
        }

        Assert.Empty(StorageRequests.VaultRequests());
        Assert.Empty(StorageRequests.ShareInboxRequests());
        Assert.Equal(0, Factory.KeyWraps.Unwraps);
        Assert.Equal(0, Factory.KeyWraps.Wraps);
        Assert.Equal(0, Factory.GraphDirectory.Calls);
        clients.ForEach(client => client.Dispose());
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task SharingAuthorizationMatrix_NonPermitted_LeaveEveryRowUnchanged(string endpointId)
    {
        var scenario = await ArrangeAsync();
        var endpoint = SharingMatrixEndpoints.Find(endpointId);
        var callers = SharingMatrixEndpoints.ShapeCallers
            .Where(caller => endpoint.ExpectedFor(caller) == HttpStatusCode.NotFound)
            .Concat(SharingMatrixEndpoints.DeniedCallers)
            .ToList();
        var before = await SnapshotAsync();

        foreach (var caller in callers)
        {
            using var client = await ArrangeCallerAsync(caller);
            await endpoint.Send(new SharingTestApi(client), scenario);
        }

        var after = await SnapshotAsync();
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(before.AuditRows, after.AuditRows);
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task SharingAuthorizationMatrix_AnonymousAndLocked_NoDirectoryCall(string endpointId)
    {
        var scenario = await ArrangeAsync();
        var endpoint = SharingMatrixEndpoints.Find(endpointId);
        var clients = new List<HttpClient>();
        foreach (var caller in new[] { CallerKind.Anonymous, CallerKind.LockedUser, CallerKind.LockedAdmin, CallerKind.LockedRecipient })
        {
            clients.Add(await ArrangeCallerAsync(caller));
        }

        Factory.GraphDirectory.Clear();

        foreach (var client in clients)
        {
            await endpoint.Send(new SharingTestApi(client), scenario);
        }

        Assert.Equal(0, Factory.GraphDirectory.Calls);
        clients.ForEach(client => client.Dispose());
    }

    [Theory]
    [MemberData(nameof(RepresentativeEndpointIds))]
    public async Task SharingAuthorizationMatrix_EveryEndpoint_NoQueryWithoutPartitionKey(string endpointId)
    {
        var scenario = await ArrangeAsync();
        var endpoint = SharingMatrixEndpoints.Find(endpointId);
        StorageRequests.Clear();

        foreach (var caller in SharingMatrixEndpoints.ShapeCallers)
        {
            using var client = await ArrangeCallerAsync(caller);
            await endpoint.Send(new SharingTestApi(client), scenario);
        }

        Assert.NotEmpty(StorageRequests.Requests);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Theory]
    [InlineData(CallerKind.User)]
    [InlineData(CallerKind.OtherUser)]
    [InlineData(CallerKind.Admin)]
    public async Task ListSharedItems_CallerWithNoGrant_ReturnsNoItemsAndNoOwnerIds(CallerKind caller)
    {
        var scenario = await ArrangeAsync();
        using var client = await ArrangeCallerAsync(caller);

        var (response, body) = await new SharingTestApi(client).ListSharedItemsAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Empty(body.Items);
        var text = await response.Content.ReadAsStringAsync(Token);
        foreach (var id in scenario.Ids)
        {
            Assert.DoesNotContain(id.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var markers in scenario.Markers)
        {
            markers.AssertAbsentFrom(text);
        }
    }

    [Fact]
    public async Task ListSharedItems_DirectRecipients_SeeOnlyWhatWasSharedWithThem()
    {
        var scenario = await ArrangeAsync();
        var expectations = new (CallerKind Caller, Guid Item)[]
        {
            (CallerKind.ShareViewer, scenario.SecretS),
            (CallerKind.ShareEditor, scenario.SecretS),
            (CallerKind.ShareManager, scenario.SecretS),
            (CallerKind.FolderViewer, scenario.FolderF),
            (CallerKind.FolderManager, scenario.FolderF),
        };

        foreach (var (caller, item) in expectations)
        {
            using var client = await ArrangeCallerAsync(caller);
            var (response, body) = await new SharingTestApi(client).ListSharedItemsAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var single = Assert.Single(body!.Items);
            Assert.Equal(item, single.ItemId);
            Assert.Equal(scenario.OwnerId, single.OwnerId);
        }
    }

    [Theory]
    [InlineData(CallerKind.ShareManager)]
    [InlineData(CallerKind.ShareViewer)]
    [InlineData(CallerKind.FolderManager)]
    public async Task Recipient_OnOwnerVaultRoutes_Returns404(CallerKind caller)
    {
        var scenario = await ArrangeAsync();
        using var client = await ArrangeCallerAsync(caller);
        var api = new VaultTestApi(client);
        var update = new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = SharingMatrixEndpoints.RequestName };
        var reveal = new RevealSecretRequestDto
        {
            Field = RevealFieldDto.Value,
            Intent = RevealIntentDto.View,
        };

        var responses = new List<HttpResponseMessage>
        {
            (await api.GetSecretAsync(scenario.SecretS)).Response,
            (await api.GetSecretAsync(scenario.SecretT)).Response,
            (await api.UpdateSecretAsync(scenario.SecretS, scenario.SecretToken, update)).Response,
            (await api.RevealSecretAsync(scenario.SecretS, reveal)).Response,
            (await api.ListHistoryAsync(scenario.SecretS)).Response,
            await api.DeleteSecretAsync(scenario.SecretS, scenario.SecretToken),
            (await api.RenameFolderAsync(scenario.FolderF, new RenameFolderRequestDto { Name = SharingMatrixEndpoints.RequestName })).Response,
            await api.DeleteFolderAsync(scenario.FolderF, FolderDeleteModeDto.DeleteContents),
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        var (listResponse, list) = await api.ListSecretsAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.DoesNotContain(list!.Items, item => scenario.Ids.Contains(item.Id));
    }

    [Theory]
    [InlineData("E22")]
    [InlineData("E24")]
    [InlineData("E25")]
    [InlineData("E28")]
    [InlineData("E32")]
    public async Task SharingRoutes_NoGrantOnRealItem_AreIdenticalToRandomIds(string route)
    {
        var scenario = await ArrangeAsync();
        using var client = await ArrangeCallerAsync(CallerKind.OtherUser);
        var api = new SharingTestApi(client);

        var real = await Send(api, route, scenario.OwnerId, route is "E25" or "E32" ? scenario.FolderF : scenario.SecretS);
        var random = await Send(api, route, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, real.StatusCode);
        Assert.Equal(random.StatusCode, real.StatusCode);
        Assert.Equal(Normalise(await random.Content.ReadAsStringAsync(Token)), Normalise(await real.Content.ReadAsStringAsync(Token)));
        Assert.Equal(random.Content.Headers.ContentType, real.Content.Headers.ContentType);
    }

    private static async Task<HttpResponseMessage> Send(SharingTestApi api, string route, Guid owner, Guid item) => route switch
    {
        "E22" => (await api.GetSharedSecretAsync(owner, item)).Response,
        "E24" => (await api.RevealSharedSecretAsync(owner, item, new RevealSecretRequestDto
        {
            Field = RevealFieldDto.Value,
            Intent = RevealIntentDto.View,
        })).Response,
        "E25" => (await api.GetSharedFolderAsync(owner, item)).Response,
        "E28" => (await api.ListSecretGrantsAsync(owner, item)).Response,
        "E32" => (await api.ListFolderGrantsAsync(owner, item)).Response,
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
    };

    private static string Normalise(string body) =>
        Regex.Replace(
            body,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\"traceId\":\"[^\"]*\"",
            "X",
            RegexOptions.CultureInvariant);

    private static async Task AssertBodyRevealsNothingAsync(HttpResponseMessage response, SharingScenario scenario, string endpointId)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        if (!response.IsSuccessStatusCode)
        {
            foreach (var markers in scenario.Markers)
            {
                markers.AssertAbsentFrom(body);
            }

            Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
            return;
        }

        if (endpointId.StartsWith(RevealEndpointPrefix, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var markers in scenario.Markers)
        {
            Assert.DoesNotContain(markers.Value, body, StringComparison.Ordinal);
            Assert.DoesNotContain(markers.Notes, body, StringComparison.Ordinal);
        }
    }

    private async Task<SharingScenario> ArrangeAsync()
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);
        Factory.GraphDirectory.Clear();
        return scenario;
    }

    private async Task<TableSnapshot> SnapshotAsync()
    {
        var rows = new List<string>();
        foreach (var table in new[] { TableNames.Vaults, TableNames.ShareInbox, TableNames.SecretAuditLog })
        {
            await foreach (var row in GetTableClient(table).QueryAsync<TableEntity>(cancellationToken: Token))
            {
                rows.Add($"{table}|{row.PartitionKey}|{row.RowKey}|{row.ETag}");
            }
        }

        var audit = (await ListAuditRowsAsync())
            .Count(row => row.GetString("EventType") is { } name && name != nameof(AuditEventType.SignIn));
        return new TableSnapshot([.. rows.Order(StringComparer.Ordinal)], audit);
    }

    private sealed record TableSnapshot(IReadOnlyList<string> Rows, int AuditRows);
}
