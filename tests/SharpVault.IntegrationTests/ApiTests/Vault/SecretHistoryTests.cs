// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed class SecretHistoryTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private const int UpdateCount = 25;

    private async Task<(SecretDto Secret, string[] Values)> CreateWithValueChangesAsync(int changes)
    {
        var values = Enumerable.Range(0, changes + 1).Select(index => $"value-{index}-{Guid.NewGuid():N}").ToArray();
        var secret = Require(await Api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = "history subject",
            Value = values[0],
        }));
        for (var index = 1; index <= changes; index++)
        {
            secret = await ReplaceValueAsync(secret, values[index]);
        }

        return (secret, values);
    }

    [Fact]
    public async Task History_AfterThreeValueChanges_ListsThreeNewestFirst()
    {
        var (secret, values) = await CreateWithValueChangesAsync(3);

        var history = Require(await Api.ListHistoryAsync(secret.Id));

        Assert.Equal(3, history.Items.Count);
        Assert.Equal(history.Items.OrderByDescending(item => item.ReplacedAt).ThenByDescending(item => item.VersionId), history.Items);
        Assert.Equal([3L, 2L, 1L], history.Items.Select(item => item.Revision));
        Assert.Equal(values[2], await RevealVersionAsync(secret.Id, history.Items[0].VersionId));
        Assert.Equal(values[1], await RevealVersionAsync(secret.Id, history.Items[1].VersionId));
        Assert.Equal(values[0], await RevealVersionAsync(secret.Id, history.Items[2].VersionId));
        Assert.Equal(values[3], await RevealValueAsync(secret.Id));
    }

    [Fact]
    public async Task HistoryReveal_ReturnsPreviousValue()
    {
        var (secret, values) = await CreateWithValueChangesAsync(1);
        var version = Assert.Single(Require(await Api.ListHistoryAsync(secret.Id)).Items);

        var revealed = await RevealVersionAsync(secret.Id, version.VersionId);

        Assert.Equal(values[0], revealed);
        Assert.Equal(values[1], await RevealValueAsync(secret.Id));
    }

    [Fact]
    public async Task HistoryRestore_SwapsValuesAndKeepsBoth()
    {
        var (secret, values) = await CreateWithValueChangesAsync(1);
        var original = Assert.Single(Require(await Api.ListHistoryAsync(secret.Id)).Items);
        var current = Require(await Api.GetSecretAsync(secret.Id));

        var restored = Require(await Api.RestoreVersionAsync(secret.Id, original.VersionId, current.ConcurrencyToken));

        Assert.NotEqual(current.ConcurrencyToken, restored.ConcurrencyToken);
        Assert.Equal(values[0], await RevealValueAsync(secret.Id));
        var history = Require(await Api.ListHistoryAsync(secret.Id));
        Assert.Equal(2, history.Items.Count);
        Assert.Contains(history.Items, item => item.VersionId == original.VersionId);
        var revealedHistory = new List<string>();
        foreach (var item in history.Items)
        {
            revealedHistory.Add(await RevealVersionAsync(secret.Id, item.VersionId));
        }

        Assert.Contains(values[1], revealedHistory);
        Assert.Contains(values[0], revealedHistory);
    }

    [Fact]
    public async Task UpdateValue_TwentyFiveTimes_KeepsTwentyNewestVersions()
    {
        var (secret, values) = await CreateWithValueChangesAsync(UpdateCount);

        var history = Require(await Api.ListHistoryAsync(secret.Id));

        Assert.Equal(VaultLimits.HistoryVersionLimit, history.Items.Count);
        Assert.Equal(values[UpdateCount - 1], await RevealVersionAsync(secret.Id, history.Items[0].VersionId));
        Assert.Equal(values[UpdateCount - VaultLimits.HistoryVersionLimit], await RevealVersionAsync(secret.Id, history.Items[^1].VersionId));
        Assert.Equal(values[UpdateCount], await RevealValueAsync(secret.Id));
        var rows = await VaultRowsAsync();
        var versionRows = rows.Count(row => row.RowKey.StartsWith($"h_{secret.Id:N}_", StringComparison.Ordinal));
        Assert.Equal(VaultLimits.HistoryVersionLimit, versionRows);
    }

    [Fact]
    public async Task History_AfterRestoreFromBin_IsIntact()
    {
        var (secret, values) = await CreateWithValueChangesAsync(2);
        var before = Require(await Api.ListHistoryAsync(secret.Id));
        var current = Require(await Api.GetSecretAsync(secret.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await Api.DeleteSecretAsync(secret.Id, current.ConcurrencyToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Api.ListHistoryAsync(secret.Id)).Response.StatusCode);

        Require(await Api.RestoreRecycledSecretAsync(secret.Id));

        var after = Require(await Api.ListHistoryAsync(secret.Id));
        Assert.Equal(before.Items.Select(item => (item.VersionId, item.Revision)), after.Items.Select(item => (item.VersionId, item.Revision)));
        Assert.Equal(values[0], await RevealVersionAsync(secret.Id, after.Items[^1].VersionId));
        Assert.Equal(values[1], await RevealVersionAsync(secret.Id, after.Items[0].VersionId));
        Assert.Equal(values[2], await RevealValueAsync(secret.Id));
    }
}
