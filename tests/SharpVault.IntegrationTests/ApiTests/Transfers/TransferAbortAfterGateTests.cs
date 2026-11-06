// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferAbortAfterGateTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string AbortBody = "{\"reason\":\"Aborted after the gate, which must refuse\"}";

    [Fact]
    public async Task Abort_AfterReleaseStarted_Returns409AndContinues()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("IVaultStore.SubmitAsync", 1, afterEffect: false);
        UseHost(CrashingTransferHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();
        await Runner.RunOnceAsync(Token);
        var releasing = await GetTransferAsync(TestUsers.Admin, transfer.Id);

        var response = await ApiAs(TestUsers.Admin).SendRawAsync(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transfer.Id)),
            AbortBody,
            releasing.ConcurrencyToken);

        Assert.True(plan.Fired, "The release write was never reached, so the case proves nothing.");
        Assert.Equal(TransferStateDto.Releasing, releasing.State);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(TransferStateDto.Releasing, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);

        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task Abort_RaceWithGate_ExactlyOneWins()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var abortFired = 0;
        Func<Task> abortAtGate = () => Task.CompletedTask;
        UseHost(RaceHost(clock, null, entry =>
            entry.EventType == AuditEventType.TransferReleaseStarted && Interlocked.Exchange(ref abortFired, 1) == 0
                ? abortAtGate()
                : Task.CompletedTask));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        HttpStatusCode? abortStatus = null;
        abortAtGate = async () =>
        {
            var staging = await GetTransferAsync(TestUsers.Admin, transfer.Id);
            var (abort, _) = await ApiAs(TestUsers.Admin).SendAsync<TransferDto>(
                HttpMethod.Post,
                VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transfer.Id)),
                new TransferDecisionRequestDto { Reason = "Aborted while the release gate is open" },
                ifMatch: staging.ConcurrencyToken);
            abortStatus = abort.StatusCode;
        };

        var current = await SettleAsync(transfer.Id, clock);

        Assert.True(abortStatus is not null, "The abort never raced the release gate.");
        var target = (await ListAllSecretsAsync(TestUsers.OtherUser)).Count;
        var source = (await ListAllSecretsAsync(TestUsers.User)).Count;
        Assert.Equal(seeded.Secrets.Count, target + source);
        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id));
        if (abortStatus == HttpStatusCode.OK)
        {
            Assert.Equal(TransferStateDto.Aborted, current.State);
            Assert.Equal(0, target);
            Assert.Equal(seeded.Secrets.Count, source);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, abortStatus);
            Assert.Equal(TransferStateDto.Completed, current.State);
            Assert.Equal(seeded.Secrets.Count, target);
            Assert.Equal(0, source);
        }
    }

    private async Task<TransferDto> SettleAsync(Guid transferId, FakeTimeProvider clock)
    {
        var current = await GetTransferAsync(TestUsers.Admin, transferId);
        for (var run = 0; run < RunBound && current.State is not (TransferStateDto.Completed or TransferStateDto.Aborted); run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transferId);
        }

        for (var cleanup = 0; cleanup < 2; cleanup++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
        }

        return await GetTransferAsync(TestUsers.Admin, transferId);
    }

    private async Task<TransferDto> ResumeUntilCompletedAsync(Guid transferId, FakeTimeProvider clock)
    {
        var current = await GetTransferAsync(TestUsers.Admin, transferId);
        for (var run = 0; run < RunBound && current.State != TransferStateDto.Completed; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transferId);
        }

        return current;
    }
}
