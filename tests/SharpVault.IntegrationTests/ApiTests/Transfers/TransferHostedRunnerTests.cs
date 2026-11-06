// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
public sealed class TransferHostedRunnerTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private static readonly TimeSpan SignalLatencyBudget = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan PollStep = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RunnerWaitTimeout = TimeSpan.FromSeconds(30);

    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    [Fact]
    public async Task HostedRunner_TransfersEnabled_CompletesOnTheApprovalSignalWithoutHandDriving()
    {
        var host = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Transfers:Enabled", "true");
            builder.UseSetting("Transfers:PollInterval", "00:05:00");
        });
        UseHost(host);
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, 3, 1);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        var started = DateTimeOffset.UtcNow;

        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var current = await PollUntilAsync(
            TestUsers.Admin,
            transfer.Id,
            candidate => candidate.State == TransferStateDto.Completed,
            CompletionTimeout);
        var elapsed = DateTimeOffset.UtcNow - started;

        Assert.Equal(TransferStateDto.Completed, current.State);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
        Assert.True(elapsed < SignalLatencyBudget, $"The approval signal took {elapsed} to reach the hosted runner.");
    }

    [Fact]
    public async Task TransferJobService_Disabled_NeverCallsTheRunner()
    {
        var runner = new CountingTransferJobRunner(throwOnRun: false);
        UseHost(WithRunner(runner, enabled: false, pollInterval: "00:00:01"));
        await WarmUpAllAsync(AllParticipants);

        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task TransferJobService_Enabled_CallsTheRunnerOnStart()
    {
        var runner = new CountingTransferJobRunner(throwOnRun: false);
        UseHost(WithRunner(runner, enabled: true, pollInterval: "00:00:01"));
        await WarmUpAllAsync(AllParticipants);

        await WaitForAsync(() => runner.Calls >= 1, RunnerWaitTimeout);

        Assert.True(runner.Calls >= 1);
    }

    [Fact]
    public async Task TransferJobService_RunnerThrows_LogsTheExceptionTypeAndKeepsPolling()
    {
        var runner = new CountingTransferJobRunner(throwOnRun: true);
        UseHost(WithRunner(runner, enabled: true, pollInterval: "00:00:01"));
        await WarmUpAllAsync(AllParticipants);

        await WaitForAsync(() => runner.Calls >= 2, RunnerWaitTimeout);
        var me = await ApiAs(TestUsers.Admin).Client.GetAsync(ApiRoutes.Me, Token);

        Assert.True(runner.Calls >= 2, $"The hosted runner stopped polling after a failure; calls={runner.Calls}.");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains(
            Logs.Entries,
            entry => entry.Level == LogLevel.Error
                && entry.Message.Contains("Transfer job run failed (InvalidOperationException)", StringComparison.Ordinal));
    }

    private WebApplicationFactory<Program> WithRunner(CountingTransferJobRunner runner, bool enabled, string pollInterval) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Transfers:Enabled", enabled ? "true" : "false");
            builder.UseSetting("Transfers:PollInterval", pollInterval);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ITransferJobRunner>();
                services.AddSingleton<ITransferJobRunner>(runner);
            });
        });

    private async Task<TransferDto> PollUntilAsync(TestUser viewer, Guid transferId, Func<TransferDto, bool> isDone, TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;
        var current = await GetTransferAsync(viewer, transferId);
        while (!isDone(current) && DateTimeOffset.UtcNow - started < timeout)
        {
            await Task.Delay(PollStep, Token);
            current = await GetTransferAsync(viewer, transferId);
        }

        return current;
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var started = DateTimeOffset.UtcNow;
        while (!condition() && DateTimeOffset.UtcNow - started < timeout)
        {
            await Task.Delay(PollStep, Token);
        }
    }

    private sealed class CountingTransferJobRunner(bool throwOnRun) : ITransferJobRunner
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<TransferRunResult> RunOnceAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (throwOnRun)
            {
                throw new InvalidOperationException("Simulated runner failure for the polling test.");
            }

            return Task.FromResult(TransferRunResult.Idle);
        }
    }
}
