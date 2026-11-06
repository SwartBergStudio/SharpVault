// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultConcurrencyTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private const int Iterations = 20;
    private const string ParentKey = "ParentKey";
    private const string FolderKey = "FolderKey";

    private static readonly Guid Owner = TestUsers.User.ObjectId;

    private async Task AssertNoCycleAsync()
    {
        var rows = await VaultRows.ListPartitionAsync(VaultsTable, Owner, Token);
        var parents = rows
            .Where(row => row.RowKey.StartsWith(VaultKeys.FolderPrefix, StringComparison.Ordinal))
            .ToDictionary(row => row.RowKey[VaultKeys.FolderPrefix.Length..], row => row.GetString(ParentKey) ?? VaultKeys.RootFolderKey);
        foreach (var start in parents.Keys)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = start;
            while (current != VaultKeys.RootFolderKey && parents.TryGetValue(current, out var parent))
            {
                Assert.True(seen.Add(current), $"The folder tree contains a cycle through {current}.");
                current = parent;
            }
        }
    }

    private async Task AssertNoActiveSecretInMissingFolderAsync()
    {
        var rows = await VaultRows.ListPartitionAsync(VaultsTable, Owner, Token);
        var activeFolders = rows
            .Where(row => row.RowKey.StartsWith(VaultKeys.FolderPrefix, StringComparison.Ordinal))
            .Select(row => row.RowKey[VaultKeys.FolderPrefix.Length..])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var secret in rows.Where(row => row.RowKey.StartsWith(VaultKeys.SecretPrefix, StringComparison.Ordinal)))
        {
            var folder = secret.GetString(FolderKey) ?? VaultKeys.RootFolderKey;
            Assert.True(
                folder == VaultKeys.RootFolderKey || activeFolders.Contains(folder),
                $"The active secret {secret.RowKey} sits in the missing or deleted folder {folder}.");
        }
    }

    [Fact]
    public async Task ParallelOppositeFolderMoves_NeverCreateACycle()
    {
        var api = ApiFor(TestUsers.User);

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var first = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = $"first-{iteration}" }));
            var second = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = $"second-{iteration}" }));

            var results = await Task.WhenAll(
                api.MoveFolderAsync(first.Id, new MoveFolderRequestDto { ParentId = second.Id }),
                api.MoveFolderAsync(second.Id, new MoveFolderRequestDto { ParentId = first.Id }));

            Assert.True(results.Count(result => result.Response.StatusCode == HttpStatusCode.OK) <= 1, $"Both moves committed in iteration {iteration}.");
            Assert.All(results, result => Assert.True(
                result.Response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
                $"Unexpected status {result.Response.StatusCode}."));
            await AssertNoCycleAsync();
        }
    }

    [Fact]
    public async Task UpdateSecret_IfMatchWildcard_Returns400AndChangesNothing()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var before = await RowsOfAsync(Owner);

        var result = await api.UpdateSecretAsync(secret.Id, "*", UpdateRequest(markers, "replaced"));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
        Assert.Equal(markers.Value, await RevealValueAsync(api, secret.Id));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("W/\"abc\"")]
    [InlineData("\"*\"")]
    [InlineData("")]
    public async Task UpdateSecret_IfMatchGarbage_Returns400(string ifMatch)
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var before = await RowsOfAsync(Owner);

        var result = await api.UpdateSecretAsync(secret.Id, ifMatch, UpdateRequest(markers, "replaced"));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task UpdateSecret_MissingIfMatch_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var before = await RowsOfAsync(Owner);

        var result = await api.UpdateSecretAsync(secret.Id, null, UpdateRequest(markers, "replaced"));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task DeleteSecret_IfMatchWildcard_Returns400AndSecretStays()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        var before = await RowsOfAsync(Owner);

        var response = await api.DeleteSecretAsync(secret.Id, "*");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
        Assert.Equal(HttpStatusCode.OK, (await api.GetSecretAsync(secret.Id)).Response.StatusCode);
    }

    [Fact]
    public async Task UpdateSecret_StaleETag_Returns409AndChangesNothing()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var first = Require(await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(markers, "first")));
        var before = await RowsOfAsync(Owner);

        var stale = await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(markers, "stale"));

        Assert.Equal(HttpStatusCode.Conflict, stale.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
        Assert.Equal("first", await RevealValueAsync(api, secret.Id));
        Assert.Equal(first.ConcurrencyToken, (await api.GetSecretAsync(secret.Id)).Body!.ConcurrencyToken);
    }

    [Fact]
    public async Task MoveSecret_StaleETag_Returns409()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "target" }));
        var secret = await CreateSecretAsync(api, markers);
        Require(await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(markers, "first")));
        var before = await RowsOfAsync(Owner);

        var stale = await api.MoveSecretAsync(secret.Id, secret.ConcurrencyToken, new MoveSecretRequestDto { FolderId = folder.Id });

        Assert.Equal(HttpStatusCode.Conflict, stale.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
        Assert.Null((await api.GetSecretAsync(secret.Id)).Body!.FolderId);
    }

    [Fact]
    public async Task HistoryRestore_StaleETag_Returns409AndHistoryUnchanged()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);
        Require(await api.UpdateSecretAsync(scenario.SecretS, scenario.SecretToken, UpdateRequest(scenario.Markers)));
        var before = await RowsOfAsync(Owner);
        var historyBefore = Require(await api.ListHistoryAsync(scenario.SecretS)).Items.Select(item => item.VersionId).ToList();

        var stale = await api.RestoreVersionAsync(scenario.SecretS, scenario.VersionV, scenario.SecretToken);

        Assert.Equal(HttpStatusCode.Conflict, stale.Response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
        Assert.Equal(historyBefore, Require(await api.ListHistoryAsync(scenario.SecretS)).Items.Select(item => item.VersionId));
    }

    [Fact]
    public async Task ParallelSecretMoveAndFolderDelete_LeaveNoActiveSecretInADeletedFolder()
    {
        var api = ApiFor(TestUsers.User);

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = $"race-{iteration}" }));
            var secret = await CreateSecretAsync(api, new PlaintextMarkers());
            var mode = iteration % 2 == 0 ? FolderDeleteModeDto.DeleteContents : FolderDeleteModeDto.MoveToParent;

            await Task.WhenAll(
                api.MoveSecretAsync(secret.Id, secret.ConcurrencyToken, new MoveSecretRequestDto { FolderId = folder.Id }),
                api.DeleteFolderAsync(folder.Id, mode));

            await AssertNoActiveSecretInMissingFolderAsync();
        }
    }

    [Fact]
    public async Task ParallelCreateInFolderAndFolderDelete_NoOrphan()
    {
        var api = ApiFor(TestUsers.User);

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = $"race-{iteration}" }));
            var mode = iteration % 2 == 0 ? FolderDeleteModeDto.DeleteContents : FolderDeleteModeDto.MoveToParent;

            await Task.WhenAll(
                api.CreateSecretAsync(NewSecretRequest(new PlaintextMarkers(), folder.Id)),
                api.DeleteFolderAsync(folder.Id, mode));

            await AssertNoActiveSecretInMissingFolderAsync();
        }
    }

    [Fact]
    public async Task CreateFolder_AtMaxFolders_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        await VaultSeeder.SeedAsync(Factory, Owner, 0, 1000, Token);
        var before = await RowsOfAsync(Owner);

        var result = await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "one-too-many" });

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        Assert.Equal(VaultMessages.FolderLimitReached, (await ProblemOfAsync(result.Response)).GetProperty("detail").GetString());
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task TwentyParallelFolderCreatesAt995_NeverExceed1000()
    {
        var api = ApiFor(TestUsers.User);
        await VaultSeeder.SeedAsync(Factory, Owner, 0, 995, Token);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(index => api.CreateFolderAsync(new CreateFolderRequestDto { Name = $"parallel-{index}" })));

        var created = results.Count(result => result.Response.StatusCode == HttpStatusCode.Created);
        var folderRows = (await VaultRows.ListPartitionAsync(VaultsTable, Owner, Token))
            .Count(row => row.RowKey.StartsWith(VaultKeys.FolderPrefix, StringComparison.Ordinal));
        Assert.InRange(folderRows, 995, 1000);
        Assert.Equal(995 + created, folderRows);
        Assert.All(results, result => Assert.True(
            result.Response.StatusCode is HttpStatusCode.Created or HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
            $"Unexpected status {result.Response.StatusCode}."));
    }

    [Fact]
    public async Task TwentyParallelFirstCreates_ShareOneMetadataKey()
    {
        var api = ApiFor(TestUsers.User);
        var markers = Enumerable.Range(0, 20).Select(_ => new PlaintextMarkers()).ToList();

        var results = await Task.WhenAll(markers.Select(marker => api.CreateSecretAsync(NewSecretRequest(marker))));

        Assert.All(results, result => Assert.Equal(HttpStatusCode.Created, result.Response.StatusCode));
        var rows = await VaultRows.ListPartitionAsync(VaultsTable, Owner, Token);
        Assert.Single(rows, row => row.RowKey == VaultKeys.KeyRingRowKey);
        var listed = Require(await api.ListSecretsAsync(pageSize: 200));
        Assert.Equal(20, listed.Items.Count);
        Assert.All(listed.Items, item => Assert.False(item.IsDamaged));
        Assert.Equal(markers.Select(marker => marker.Name).Order(StringComparer.Ordinal), listed.Items.Select(item => item.Name!).Order(StringComparer.Ordinal));
        foreach (var result in results)
        {
            Assert.Equal(markers.Single(marker => marker.Name == result.Body!.Name).Value, await RevealValueAsync(api, result.Body!.Id));
        }
    }

    [Fact]
    public async Task ParallelUpdatesWithSameETag_ExactlyOneSucceeds()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(index => api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(markers, $"value-{index}"))));

        Assert.Single(results, result => result.Response.StatusCode == HttpStatusCode.OK);
        Assert.All(results.Where(result => result.Response.StatusCode != HttpStatusCode.OK), result => Assert.Equal(HttpStatusCode.Conflict, result.Response.StatusCode));
        var winner = await RevealValueAsync(api, secret.Id);
        Assert.StartsWith("value-", winner, StringComparison.Ordinal);
        Assert.Single(Require(await api.ListHistoryAsync(secret.Id)).Items);
    }

    [Fact]
    public async Task ParallelRevealsAndUpdate_RevealReturnsOldOrNewValueNeverMixed()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var replacement = markers.Value + "-replacement";

        var reveals = Enumerable.Range(0, 10).Select(_ => api.RevealSecretAsync(secret.Id, ViewValue)).ToList();
        var update = api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, UpdateRequest(markers, replacement));
        await Task.WhenAll(reveals.Cast<Task>().Append(update));

        Assert.Equal(HttpStatusCode.OK, (await update).Response.StatusCode);
        foreach (var reveal in reveals)
        {
            var result = await reveal;
            Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
            Assert.Contains(result.Body!.Value, new[] { markers.Value, replacement });
        }
    }
}
