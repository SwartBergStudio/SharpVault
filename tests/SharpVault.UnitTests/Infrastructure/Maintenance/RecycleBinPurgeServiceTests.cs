// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Maintenance;

public sealed class RecycleBinPurgeServiceTests : IDisposable
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly TimerCountingTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingLogger<RecycleBinPurgeService> _logger = new();
    private readonly SignalingPurger _purger = new();

    public void Dispose() => _purger.Dispose();

    private RecycleBinPurgeService Create(bool enabled = true) => new(
        _purger,
        Options.Create(new RecycleBinPurgeOptions { Enabled = enabled, InitialDelay = InitialDelay, Interval = Interval }),
        _time,
        _logger);

    [Fact]
    public async Task Disabled_NeverCallsPurger()
    {
        using var service = Create(enabled: false);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.ExecuteTask!.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        _time.Advance(Interval + InitialDelay + Interval);

        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);
        Assert.Equal(0, _purger.Calls);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Enabled_RunsAfterInitialDelay()
    {
        using var service = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _time.WaitForTimersAsync(1);

        _time.Advance(InitialDelay - TimeSpan.FromSeconds(1));
        Assert.Equal(0, _purger.Calls);
        _time.Advance(TimeSpan.FromSeconds(1));
        await _purger.WaitForCallAsync(1);
        await _time.WaitForTimersAsync(2);

        Assert.Equal(1, _purger.Calls);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Enabled_RunsAgainEveryInterval()
    {
        using var service = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _time.WaitForTimersAsync(1);
        _time.Advance(InitialDelay);
        await _purger.WaitForCallAsync(1);
        await _time.WaitForTimersAsync(2);

        _time.Advance(Interval);
        await _purger.WaitForCallAsync(2);
        _time.Advance(Interval);
        await _purger.WaitForCallAsync(3);

        Assert.Equal(3, _purger.Calls);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PurgerThrows_LogsAndContinues()
    {
        _purger.ThrowOnCall = 1;
        using var service = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _time.WaitForTimersAsync(1);
        _time.Advance(InitialDelay);
        await _purger.WaitForCallAsync(1);
        await _time.WaitForTimersAsync(2);

        _time.Advance(Interval);
        await _purger.WaitForCallAsync(2);

        Assert.Equal(2, _purger.Calls);
        Assert.False(service.ExecuteTask!.IsCompleted);
        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Contains(nameof(InvalidOperationException), error.Message, StringComparison.Ordinal);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PurgerCancelledByAStorageTimeout_LogsAndKeepsTheServiceRunning()
    {
        _purger.ThrowTimeoutCancellationOnCall = 1;
        using var service = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _time.WaitForTimersAsync(1);
        _time.Advance(InitialDelay);
        await _purger.WaitForCallAsync(1);
        await _time.WaitForTimersAsync(2);

        _time.Advance(Interval);
        await _purger.WaitForCallAsync(2);

        Assert.Equal(2, _purger.Calls);
        Assert.False(service.ExecuteTask!.IsCompleted);
        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Contains(nameof(TaskCanceledException), error.Message, StringComparison.Ordinal);
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PurgerThrows_LogDoesNotContainTheExceptionTextOrAttachTheException()
    {
        _purger.ThrowOnCall = 1;
        using var service = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _time.WaitForTimersAsync(1);

        _time.Advance(InitialDelay);
        await _purger.WaitForCallAsync(1);
        await _time.WaitForTimersAsync(2);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.DoesNotContain(SignalingPurger.FailureText, error.Message, StringComparison.Ordinal);
        Assert.Null(error.Exception);
    }

    [Fact]
    public async Task Stop_CancelsCleanly()
    {
        using var service = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _time.WaitForTimersAsync(1);
        _time.Advance(InitialDelay);
        await _purger.WaitForCallAsync(1);
        await _time.WaitForTimersAsync(2);

        await service.StopAsync(TestContext.Current.CancellationToken);
        _time.Advance(Interval + Interval);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(1, _purger.Calls);
        Assert.DoesNotContain(_logger.Records, record => record.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Stop_DuringInitialDelay_NeverCallsPurger()
    {
        using var service = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _time.WaitForTimersAsync(1);

        await service.StopAsync(TestContext.Current.CancellationToken);
        _time.Advance(InitialDelay + Interval);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully, service.ExecuteTask.Status + " " + service.ExecuteTask.Exception);
        Assert.Equal(0, _purger.Calls);
    }

    [Fact]
    public async Task Stop_DuringAPurge_CancelsThePurgerWithoutLoggingAnError()
    {
        _purger.BlockUntilCancelled = true;
        using var service = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);
        await _time.WaitForTimersAsync(1);
        _time.Advance(InitialDelay);
        await _purger.WaitForCallAsync(1);

        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.DoesNotContain(_logger.Records, record => record.Level >= LogLevel.Error);
    }

    private sealed class TimerCountingTimeProvider(DateTimeOffset start) : FakeTimeProvider(start)
    {
        private int _timers;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            Interlocked.Increment(ref _timers);
            return timer;
        }

        public async Task WaitForTimersAsync(int expected)
        {
            var deadline = DateTimeOffset.UtcNow + Timeout;
            while (Volatile.Read(ref _timers) < expected)
            {
                if (DateTimeOffset.UtcNow > deadline)
                {
                    throw new TimeoutException("The service never armed its timer.");
                }

                await Task.Yield();
            }
        }
    }

    private sealed class SignalingPurger : IRecycleBinPurger, IDisposable
    {
        public const string FailureText = "storage said secret-internal-detail";

        private readonly SemaphoreSlim _called = new(0);
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Dispose() => _called.Dispose();

        public int ThrowOnCall { get; set; }

        public int ThrowTimeoutCancellationOnCall { get; set; }

        public bool BlockUntilCancelled { get; set; }

        public async Task<RecycleBinPurgeResult> PurgeExpiredAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            _called.Release();
            if (BlockUntilCancelled)
            {
                await Task.Delay(Timeout, cancellationToken);
            }

            if (call == ThrowTimeoutCancellationOnCall)
            {
                throw new TaskCanceledException("the storage request timed out");
            }

            if (call == ThrowOnCall)
            {
                throw new InvalidOperationException(FailureText);
            }

            return RecycleBinPurgeResult.NotRun;
        }

        public async Task WaitForCallAsync(int expectedCalls)
        {
            var deadline = DateTimeOffset.UtcNow + Timeout;
            while (Calls < expectedCalls)
            {
                if (!await _called.WaitAsync(Timeout, TestContext.Current.CancellationToken) || DateTimeOffset.UtcNow > deadline)
                {
                    throw new TimeoutException($"The purger was called {Calls} times; {expectedCalls} were expected.");
                }
            }
        }
    }
}
