// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Dtos.Transfers;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using Xunit;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferLeaseRenewalTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(10);

    private static readonly TimeSpan StorageCallCost = TimeSpan.FromSeconds(50);

    private const int SeededSecrets = 3;

    private const int SeededFolders = 2;

    private const string LeaseDurationSetting = "Transfers:LeaseDuration";

    private const string LeaseLostText = "lease was lost";

    [Fact]
    public async Task LongRun_RenewsItsLeaseAsItGoes_AndCompletesWithoutLosingIt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseHost(LeaseHost(clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, SeededSecrets, SeededFolders);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var started = clock.GetUtcNow();

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.True(clock.GetUtcNow() - started > LeaseDuration, "The run did not outlive one lease, so renewal was never needed.");
        AssertLogsDoNotContain(LeaseLostText);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
    }

    private WebApplicationFactory<Program> LeaseHost(FakeTimeProvider clock) =>
        RaceHost(clock, (method, _) =>
        {
            if (method.DeclaringType == typeof(IVaultStore))
            {
                clock.Advance(StorageCallCost);
            }

            return Task.CompletedTask;
        }, null)
        .WithWebHostBuilder(builder => builder.UseSetting(LeaseDurationSetting, LeaseDuration.ToString("c")));
}
