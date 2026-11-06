// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Dtos.Transfers;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferStorageLeakageTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    [Fact]
    public async Task StagingRows_ContainNoPlaintextMarker()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("ITransferStagingStore.StageSecretsAsync", 1, afterEffect: true);
        UseHost(CrashingTransferHost(plan, clock));
        await WarmUpAllAsync(AllParticipants);
        var markers = new PlaintextMarkers();
        await CreateSecretAsync(ApiAs(TestUsers.User), markers);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        await Runner.RunOnceAsync(Token);

        Assert.True(plan.Fired, "The staging write was never reached, so the case proves nothing.");
        Assert.True(await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id) > 0, "No staging row exists to inspect.");
        var rows = await AllRowsAsync(VaultsTable);
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            markers.AssertAbsentFrom(RowText(row));
        }
    }

    [Fact]
    public async Task JobRow_ContainsNoPlaintextMarker()
    {
        var markers = new PlaintextMarkers();
        await WarmUpAllAsync(AllParticipants);
        await CreateSecretAsync(ApiAs(TestUsers.User), markers);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var rows = await AllRowsAsync(GetTableClient(TableNames.TransferJobs));

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            markers.AssertAbsentFrom(RowText(row));
        }
    }

    private static async Task<List<TableEntity>> AllRowsAsync(TableClient table)
    {
        var rows = new List<TableEntity>();
        await foreach (var row in table.QueryAsync<TableEntity>(cancellationToken: Token))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static string RowText(TableEntity row) =>
        string.Join('\n', row.Keys.Select(key => row[key]?.ToString()));
}
