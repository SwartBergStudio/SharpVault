// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.Infrastructure.Users;

namespace SharpVault.UnitTests.Infrastructure.Users;

[Trait("Category", "Security")]
public sealed class SessionRevocationPurgeServiceTests
{
    private static readonly TimeSpan LongInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task SessionRevocationPurgeService_Disabled_NeverCallsTheLedger()
    {
        var ledger = Substitute.For<ISessionRevocationLedger>();
        var service = CreateService(ledger, enabled: false);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        await ledger.DidNotReceiveWithAnyArgs().PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SessionRevocationPurgeService_LedgerThrows_KeepsRunningAndStopsCleanly()
    {
        var called = new TaskCompletionSource();
        var ledger = Substitute.For<ISessionRevocationLedger>();
        ledger.PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            called.TrySetResult();
            return Task.FromException<int>(new InvalidOperationException("The ledger is down."));
        });
        var service = CreateService(ledger, enabled: true);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await called.Task.WaitAsync(CallTimeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        await ledger.Received(1).PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private static SessionRevocationPurgeService CreateService(ISessionRevocationLedger ledger, bool enabled) =>
        new(
            ledger,
            Options.Create(new RecycleBinPurgeOptions { Enabled = enabled, InitialDelay = TimeSpan.Zero, Interval = LongInterval }),
            TimeProvider.System,
            NullLogger<SessionRevocationPurgeService>.Instance);
}
