// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkPurgeServiceTests
{
    private const string FailureText = "storage said secret-internal-detail";
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private readonly IOneTimeLinkPurger _purger = Substitute.For<IOneTimeLinkPurger>();
    private readonly RecordingLogger<OneTimeLinkPurgeService> _logger = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private OneTimeLinkPurgeService Create(OneTimeLinkOptions options) => new(
        _purger,
        Options.Create(options),
        TimeProvider.System,
        _logger);

    [Fact]
    public async Task PurgeDisabled_NeverCallsThePurger()
    {
        using var service = Create(new OneTimeLinkOptions { PurgeEnabled = false });

        await service.StartAsync(Token);
        await service.ExecuteTask!.WaitAsync(WaitLimit, Token);

        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
        await _purger.DidNotReceive().PurgeExpiredAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgerThrows_LogsTheTypeAndKeepsRunning()
    {
        var secondCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _purger.PurgeExpiredAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                return Task.FromException(new InvalidOperationException(FailureText));
            }

            secondCall.TrySetResult();
            return Task.CompletedTask;
        });
        using var service = Create(new OneTimeLinkOptions
        {
            PurgeInitialDelay = TimeSpan.Zero,
            PurgeInterval = TimeSpan.FromMilliseconds(10),
        });

        await service.StartAsync(Token);
        await secondCall.Task.WaitAsync(WaitLimit, Token);
        await service.StopAsync(Token);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Contains(nameof(InvalidOperationException), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(FailureText, error.Message, StringComparison.Ordinal);
        Assert.Null(error.Exception);
    }
}
