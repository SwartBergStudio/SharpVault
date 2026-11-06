// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferRaceTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string ExpandedValue = "two";
    private const string OriginalValue = "one";
    private const int LeaseStallSeconds = 180;
    private const int SecretCount = 4;

    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    [Fact]
    public async Task OwnerRecyclesAndRestores_DuringTheReleaseWindow_NothingIsLostOrDuplicated()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Func<Task> ownerActions = () => Task.CompletedTask;
        UseHost(RaceHost(clock, BeforeFirstReleasePack(() => ownerActions()), null));
        await WarmUpAllAsync(AllParticipants);
        var api = ApiAs(TestUsers.User);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Binned folder" }));
        var inFolder = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "InFolder", Value = OriginalValue, FolderId = folder.Id }));
        var (update, _) = await api.UpdateSecretAsync(inFolder.Id, inFolder.ConcurrencyToken, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "InFolder", ReplaceValue = true, Value = ExpandedValue });
        Assert.True(update.IsSuccessStatusCode, update.StatusCode.ToString());
        var loose = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Loose", Value = "loose" }));
        var binned = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "AlreadyBinned", Value = "binned" }));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(binned.Id, binned.ConcurrencyToken)).StatusCode);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var ownerStatuses = new List<HttpStatusCode>();
        ownerActions = async () =>
        {
            ownerStatuses.Add((await api.DeleteSecretAsync(loose.Id, loose.ConcurrencyToken)).StatusCode);
            ownerStatuses.Add((await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
            var (restore, _) = await api.RestoreRecycledSecretAsync(binned.Id);
            ownerStatuses.Add(restore.StatusCode);
        };

        var settled = await ResumeUntilCompletedAsync(transfer.Id, clock, Runner);

        Assert.Equal(HttpStatusCode.NoContent, ownerStatuses[0]);
        Assert.Equal(HttpStatusCode.NoContent, ownerStatuses[1]);
        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
        var targetActive = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(secret => secret.Id);
        var targetBin = Require(await ApiAs(TestUsers.OtherUser).ListRecycleBinAsync());
        var placed = targetActive
            .Concat(targetBin.Items.Where(item => item.Kind == RecycleBinItemKindDto.Secret).Select(item => item.Id))
            .ToList();
        Assert.Equal(placed.Count, placed.Distinct().Count());
        Assert.Contains(inFolder.Id, placed);
        Assert.Contains(loose.Id, placed);
        Assert.Contains(binned.Id, placed);
        Assert.Equal(3, placed.Count);
    }

    [Fact]
    public async Task Runner_LeaseLostMidRun_StopsAndAnotherRunnerFinishes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var stalled = 0;
        UseHost(RaceHost(clock, (method, args) =>
        {
            if (IsReleasePack(method, args) && Interlocked.Exchange(ref stalled, 1) == 0)
            {
                clock.Advance(TimeSpan.FromSeconds(LeaseStallSeconds));
            }

            return Task.CompletedTask;
        }, null));
        await WarmUpAllAsync(AllParticipants);
        var api = ApiAs(TestUsers.User);
        for (var index = 0; index < SecretCount; index++)
        {
            var created = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "S" + index, Value = "a" }));
            var (edit, _) = await api.UpdateSecretAsync(created.Id, created.ConcurrencyToken, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "S" + index, ReplaceValue = true, Value = "b" });
            Assert.True(edit.IsSuccessStatusCode, edit.StatusCode.ToString());
        }

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);

        Assert.Equal(1, Volatile.Read(ref stalled));
        Assert.True(
            Logs.Entries.Any(entry => entry.Message.Contains("lease was lost", StringComparison.Ordinal)),
            "The stalled run never lost its lease, so the case proves nothing.");
        var anotherRunner = ActivatorUtilities.CreateInstance<TransferJobRunner>(Host.Services);
        var settled = await ResumeUntilCompletedAsync(transfer.Id, clock, anotherRunner);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
        var targetIds = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(secret => secret.Id).ToList();
        Assert.Equal(SecretCount, targetIds.Count);
        Assert.Equal(targetIds.Count, targetIds.Distinct().Count());
    }

    [Fact]
    public async Task OrphanVersionRow_DoesNotBlockTheTransfer()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseHost(RaceHost(clock, null, null));
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 0);
        var orphan = new TableEntity(VaultKeys.PartitionKey(TestUsers.User.ObjectId), VaultKeys.VersionRowKey(Guid.NewGuid(), Guid.NewGuid()))
        {
            ["CreatedAt"] = DateTimeOffset.UtcNow,
        };
        await VaultsTable.AddEntityAsync(orphan, Token);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await ResumeUntilCompletedAsync(transfer.Id, clock, Runner);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
        var targetIds = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(secret => secret.Id).ToList();
        Assert.Equal(seeded.Secrets.Count, targetIds.Count);
        Assert.Equal(targetIds.Count, targetIds.Distinct().Count());
    }

    private async Task<TransferDto> ResumeUntilCompletedAsync(Guid transferId, FakeTimeProvider clock, ITransferJobRunner runner)
    {
        var current = await GetTransferAsync(TestUsers.Admin, transferId);
        for (var run = 0; run < RunBound && current.State != TransferStateDto.Completed; run++)
        {
            await runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transferId);
        }

        return current;
    }
}
