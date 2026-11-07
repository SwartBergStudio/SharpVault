// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using SharpVault.UI.Shared.Services.Clipboard;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Services;

[Trait("Category", "Security")]
public sealed class SecretClipboardTests : IAsyncLifetime
{
    private const string Secret = "hunter2-clipboard-secret";
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan PollLimit = TimeSpan.FromSeconds(5);

    private readonly FakeClipboard _clipboard = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingLogger<SecretClipboard> _logger = new();
    private readonly SecretClipboard _secretClipboard;

    public SecretClipboardTests()
    {
        _secretClipboard = new SecretClipboard(_clipboard, new VaultUiSettings(Delay, Delay), _time, _logger);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _secretClipboard.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + PollLimit;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(Poll, TestContext.Current.CancellationToken);
        }

        Assert.True(condition(), "The condition was not met in time.");
    }

    private static async Task SettleAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }

        await Task.Delay(Poll, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Copy_Success_WritesTheTextAndReturnsTrue()
    {
        var copied = await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);

        Assert.True(copied);
        Assert.Equal([Secret], _clipboard.Writes);
        Assert.Equal(0, _clipboard.ClearCalls);
    }

    [Fact]
    public async Task SecretClipboard_AfterDelay_ClearsClipboard()
    {
        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);

        _time.Advance(Delay - TimeSpan.FromMilliseconds(1));
        await SettleAsync();
        Assert.Equal(0, _clipboard.ClearCalls);
        Assert.Equal(Secret, _clipboard.Current);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        await WaitUntilAsync(() => _clipboard.ClearCalls == 1);

        Assert.Equal(string.Empty, _clipboard.Current);
    }

    [Fact]
    public async Task SecretClipboard_SecondCopy_RestartsTimer()
    {
        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(20));
        await SettleAsync();

        await _secretClipboard.CopyAsync("second-secret", TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(20));
        await SettleAsync();
        Assert.Equal(0, _clipboard.ClearCalls);
        Assert.Equal("second-secret", _clipboard.Current);

        _time.Advance(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => _clipboard.ClearCalls == 1);
        await SettleAsync();

        Assert.Equal(1, _clipboard.ClearCalls);
    }

    [Fact]
    public async Task SecretClipboard_Dispose_ClearsImmediately()
    {
        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);

        await _secretClipboard.DisposeAsync();

        Assert.Equal(1, _clipboard.ClearCalls);
        Assert.Equal(string.Empty, _clipboard.Current);
        _time.Advance(Delay * 2);
        await SettleAsync();
        Assert.Equal(1, _clipboard.ClearCalls);
    }

    [Fact]
    public async Task ClearNow_PendingClear_ClearsOnceAndCancelsTheTimer()
    {
        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);

        await _secretClipboard.ClearNowAsync(TestContext.Current.CancellationToken);
        await _secretClipboard.ClearNowAsync(TestContext.Current.CancellationToken);
        _time.Advance(Delay * 2);
        await SettleAsync();

        Assert.Equal(1, _clipboard.ClearCalls);
    }

    [Fact]
    public async Task SecretClipboard_WriteThrows_ReturnsFalseAndSchedulesNothing()
    {
        _clipboard.FailWritesWithJavaScriptError("denied for hunter2-clipboard-secret");

        var copied = await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);
        _time.Advance(Delay * 2);
        await SettleAsync();

        Assert.False(copied);
        Assert.Empty(_clipboard.Writes);
        Assert.Equal(0, _clipboard.ClearCalls);
        await _secretClipboard.DisposeAsync();
        Assert.Equal(0, _clipboard.ClearCalls);
    }

    [Fact]
    public async Task SecretClipboard_WriteThrows_LogsTypeOnly()
    {
        _clipboard.FailWritesWithJavaScriptError("denied for hunter2-clipboard-secret");

        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);

        var text = _logger.AllText();
        Assert.Contains(nameof(JSException), text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copy_WriteThrowsAfterEarlierCopy_EarlierClearStillRuns()
    {
        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);
        _clipboard.FailWritesWithJavaScriptError("denied");

        var copied = await _secretClipboard.CopyAsync("other", TestContext.Current.CancellationToken);
        _time.Advance(Delay);
        await WaitUntilAsync(() => _clipboard.ClearCalls == 1);

        Assert.False(copied);
    }

    [Fact]
    public async Task Copy_OtherThanJavaScriptFailure_Propagates()
    {
        _clipboard.WriteFailure = new InvalidOperationException("unexpected");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SecretClipboard_ClearThrows_LogsTypeOnlyAndDoesNotThrow()
    {
        _clipboard.FailClearsWithJavaScriptError("denied for hunter2-clipboard-secret");
        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);

        _time.Advance(Delay);
        await WaitUntilAsync(() => _clipboard.ClearCalls == 1);
        await WaitUntilAsync(() => _logger.Records.Count == 1);

        var text = _logger.AllText();
        Assert.Contains(nameof(JSException), text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, text, StringComparison.Ordinal);
        await _secretClipboard.DisposeAsync();
        Assert.Equal(1, _clipboard.ClearCalls);
    }

    [Theory]
    [InlineData("js")]
    [InlineData("disconnected")]
    public async Task ClearNow_ClearThrowsKnownFailure_DoesNotThrow(string kind)
    {
        var failure = ClearFailure(kind);
        _clipboard.ClearFailure = failure;
        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);

        var exception = await Record.ExceptionAsync(() => _secretClipboard.ClearNowAsync(TestContext.Current.CancellationToken));

        Assert.Null(exception);
        Assert.Equal(1, _clipboard.ClearCalls);
        Assert.Contains(failure.GetType().Name, _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_AfterCopyAndClear_NeverContainTheCopiedText()
    {
        await _secretClipboard.CopyAsync(Secret, TestContext.Current.CancellationToken);
        _time.Advance(Delay);
        await WaitUntilAsync(() => _clipboard.ClearCalls == 1);

        Assert.DoesNotContain("hunter2", _logger.AllText(), StringComparison.Ordinal);
    }

    private static Exception ClearFailure(string kind) => kind switch
    {
        "js" => new JSException("clipboard blocked"),
        "disconnected" => new JSDisconnectedException("circuit gone"),
        _ => new OperationCanceledException(),
    };
}
