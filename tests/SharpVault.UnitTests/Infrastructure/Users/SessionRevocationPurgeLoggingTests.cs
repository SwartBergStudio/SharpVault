// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.Infrastructure.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Users;

[Trait("Category", "Security")]
public sealed class SessionRevocationPurgeLoggingTests
{
    private static readonly TimeSpan LongInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private const string LedgerFailureMessage = "The ledger is down with a connection string.";

    [Fact]
    public async Task SessionRevocationPurgeService_LedgerThrows_LogsTheTypeAndKeepsRunning()
    {
        var called = new TaskCompletionSource();
        var ledger = Substitute.For<ISessionRevocationLedger>();
        ledger.PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            called.TrySetResult();
            return Task.FromException<int>(new InvalidOperationException(LedgerFailureMessage));
        });
        var logger = new CollectingLogger<SessionRevocationPurgeService>();
        var service = new SessionRevocationPurgeService(
            ledger,
            Options.Create(new RecycleBinPurgeOptions { Enabled = true, InitialDelay = TimeSpan.Zero, Interval = LongInterval }),
            TimeProvider.System,
            logger);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await called.Task.WaitAsync(CallTimeout, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Contains(nameof(InvalidOperationException), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LedgerFailureMessage, entry.Message, StringComparison.Ordinal);
        await ledger.Received(1).PurgeExpiredAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
