// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Text.Json;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Layout;

[Trait("Category", "Security")]
public sealed class IdleMonitorScriptTests
{
    private const string FormId = "sign-out-form";
    private const int Timeout = 60000;
    private const int HalfMinute = 30000;

    private static readonly string ScriptPath = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared", "wwwroot", "js", "idle-monitor.js");
    private static readonly string HarnessPath = Path.Combine(RepositoryRoot.Path, "tests", "SharpVault.UnitTests", "UiShared", "Layout", "idle-monitor.harness.mjs");

    private static readonly JsonSerializerOptions ResultOptions = new(JsonSerializerDefaults.Web);

    private static object Start(string? session = null) => new { op = "start", timeoutMs = Timeout, session };

    private static object Advance(int ms) => new { op = "advance", ms };

    private static object Activity(string name = "keydown") => new { op = "activity", @event = name };

    private static object Stop() => new { op = "stop" };

    private static object Copy(string text = "secret") => new { op = "copy", text };

    private static object ClearClipboard() => new { op = "clearClipboard" };

    private static object OtherTabActivity() => new { op = "storage" };

    private static object ClipboardMode(string mode) => new { op = "clipboardMode", mode };

    private static object Navigate(bool loadClipboardModule = true) => new { op = "navigate", load = loadClipboardModule };

    private static object Focus() => new { op = "focus" };

    private static object Visibility(string state) => new { op = "visibility", state };

    [Fact]
    public async Task Monitor_NoActivityUntilTimeout_SubmitsTheSignOutFormOnce()
    {
        var result = await RunAsync(Start(), Advance(Timeout));

        Assert.Equal(1, result.Submits);
    }

    [Theory]
    [InlineData("pointerdown")]
    [InlineData("keydown")]
    public async Task Monitor_ActivityBeforeTimeout_DelaysTheSubmit(string activity)
    {
        var before = await RunAsync(Start(), Advance(Timeout - 1000), Activity(activity), Advance(Timeout - 1000));
        var after = await RunAsync(Start(), Advance(Timeout - 1000), Activity(activity), Advance(Timeout));

        Assert.Equal(0, before.Submits);
        Assert.Equal(1, after.Submits);
    }

    [Fact]
    public async Task Monitor_ActivityInAnotherTab_DelaysTheSubmit()
    {
        var delayed = await RunAsync(Start(), Advance(50000), OtherTabActivity(), Advance(20000));
        var expired = await RunAsync(Start(), Advance(50000), OtherTabActivity(), Advance(Timeout));

        Assert.Equal(0, delayed.Submits);
        Assert.Equal(1, expired.Submits);
    }

    [Fact]
    public async Task Monitor_Stopped_NeverSubmitsAndLeavesNoTimer()
    {
        var result = await RunAsync(Start(), Advance(1000), Stop(), Advance(Timeout * 3));

        Assert.Equal(0, result.Submits);
        Assert.Equal(0, result.Timers);
    }

    [Fact]
    public async Task Monitor_ActivityAfterStop_DoesNotRestartTheTimer()
    {
        var result = await RunAsync(Start(), Stop(), Activity(), Advance(Timeout * 3));

        Assert.Equal(0, result.Submits);
    }

    [Fact]
    public async Task Monitor_StartedTwice_SubmitsOnce()
    {
        var result = await RunAsync(Start(), Advance(1000), Start(), Advance(Timeout));

        Assert.Equal(1, result.Submits);
    }

    [Fact]
    public async Task Monitor_SubmitDoesNotComplete_RetriesAfterTenSeconds()
    {
        var result = await RunAsync(Start(), Advance(Timeout), Advance(10000));

        Assert.Equal(2, result.Submits);
    }

    [Fact]
    public async Task Monitor_CopyPending_ClearsTheClipboardBeforeSubmitting()
    {
        var result = await RunWithClipboardAsync("ok", Start(), Copy(), Advance(Timeout));

        Assert.Equal(1, result.Submits);
        Assert.Equal(["write:secret", "write:", "submit"], result.Order);
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("throw")]
    [InlineData("missing")]
    public async Task Monitor_ClipboardClearFailsOrIsMissing_StillSubmits(string clipboard)
    {
        var result = await RunWithClipboardAsync(clipboard, Start(), Advance(1000), Copy(), Advance(Timeout));

        Assert.Equal(1, result.Submits);
    }

    [Fact]
    public async Task Monitor_ClearHangs_SubmitsWithinTheGuardTimeout()
    {
        var before = await RunWithClipboardAsync("hang", Start(), Copy(), Advance(Timeout));
        var after = await RunWithClipboardAsync("hang", Start(), Copy(), Advance(Timeout), Advance(2000));

        Assert.Equal(0, before.Submits);
        Assert.Equal(1, after.Submits);
    }

    [Fact]
    public async Task Monitor_NothingPending_NeverTouchesTheClipboard()
    {
        var result = await RunWithClipboardAsync("ok", Start(), Advance(Timeout));

        Assert.Equal(1, result.Submits);
        Assert.Empty(result.Writes);
    }

    [Fact]
    public async Task Monitor_CopyAlreadyCleared_NeverTouchesTheClipboardAgain()
    {
        var result = await RunWithClipboardAsync("ok", Start(), Copy(), ClearClipboard(), Advance(Timeout));

        Assert.Equal(1, result.Submits);
        Assert.Equal(["secret", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_RejectedWhileUnfocused_RetriesWhenTheWindowRegainsFocus()
    {
        var result = await RunWithClipboardAsync("reject", Copy(), ClearClipboard(), ClipboardMode("ok"), Focus());

        Assert.Equal(["secret", "", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_RetriedSuccessfully_DoesNotWriteAgainOnLaterFocus()
    {
        var result = await RunWithClipboardAsync("reject", Copy(), ClearClipboard(), ClipboardMode("ok"), Focus(), Focus(), Visibility("visible"));

        Assert.Equal(["secret", "", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_RejectedWhileUnfocused_RetriesWhenThePageBecomesVisible()
    {
        var result = await RunWithClipboardAsync("reject", Copy(), Visibility("hidden"), ClearClipboard(), ClipboardMode("ok"), Visibility("visible"));

        Assert.Equal(["secret", "", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_RejectedWhileUnfocused_DoesNotRetryWhileThePageIsHidden()
    {
        var result = await RunWithClipboardAsync("reject", Copy(), Visibility("hidden"), ClearClipboard(), ClipboardMode("ok"), Visibility("hidden"));

        Assert.Equal(["secret", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_RetryRejectedAgain_KeepsRetryingOnTheNextFocus()
    {
        var result = await RunWithClipboardAsync("reject", Copy(), ClearClipboard(), Focus(), ClipboardMode("ok"), Focus());

        Assert.Equal(["secret", "", "", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_SucceedsFirstTime_NeverRetries()
    {
        var result = await RunWithClipboardAsync("ok", Copy(), ClearClipboard(), Focus(), Visibility("visible"));

        Assert.Equal(["secret", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_RejectedThenIdleLockClears_NoRetryAfterwards()
    {
        var result = await RunWithClipboardAsync("reject", Start(), Copy(), ClearClipboard(), ClipboardMode("ok"), Advance(Timeout), Focus());

        Assert.Equal(["secret", "", ""], result.Writes);
        Assert.Equal(1, result.Submits);
    }

    [Fact]
    public async Task Clear_IdleSignOutWhileUnfocused_ClearsWhenTheSignedOutPageRegainsFocus()
    {
        var result = await RunWithClipboardAsync("reject", Start(), Copy(), Advance(Timeout), Navigate(), ClipboardMode("ok"), Focus());

        Assert.Equal(["secret", "", "", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_IdleSignOutClearedTheClipboard_SignedOutPageNeverWrites()
    {
        var result = await RunWithClipboardAsync("ok", Start(), Copy(), Advance(Timeout), Navigate(), Focus());

        Assert.Equal(["secret", ""], result.Writes);
    }

    [Fact]
    public async Task Clear_PageLoadedWithoutTheClipboardModule_NeverClearsAfterSignOut()
    {
        var result = await RunWithClipboardAsync("reject", Start(), Copy(), Advance(Timeout), Navigate(false), ClipboardMode("ok"), Focus());

        Assert.Equal(["secret", ""], result.Writes);
    }

    [Fact]
    public async Task Monitor_ReopenedAfterTimeoutInTheSameSession_SubmitsTheSignOutForm()
    {
        var result = await RunAsync(Start("a"), Stop(), Advance(Timeout), Start("a"), Advance(0));

        Assert.Equal(1, result.Submits);
    }

    [Fact]
    public async Task Monitor_ReloadedPartWayThroughTimeout_KeepsTheRemainingTime()
    {
        var beforeLimit = await RunAsync(Start("a"), Advance(40000), Stop(), Start("a"), Advance(19999));
        var atLimit = await RunAsync(Start("a"), Advance(40000), Stop(), Start("a"), Advance(20000));

        Assert.Equal(0, beforeLimit.Submits);
        Assert.Equal(1, atLimit.Submits);
    }

    [Fact]
    public async Task Monitor_ReopenedInANewSession_StartsFresh()
    {
        var result = await RunAsync(Start("a"), Stop(), Advance(Timeout), Start("b"), Advance(0));

        Assert.Equal(0, result.Submits);
    }

    [Fact]
    public async Task Monitor_ActivityEveryMinute_SendsOneHeartbeatPerMinute()
    {
        var steps = new List<object> { Start("a") };
        for (var burst = 0; burst < 10; burst++)
        {
            steps.Add(Activity());
            steps.Add(Advance(HalfMinute));
        }

        var result = await RunAsync(steps.ToArray());

        Assert.Equal(5, result.Heartbeats);
    }

    [Fact]
    public async Task Monitor_OtherTabActivity_SendsAHeartbeat()
    {
        var result = await RunAsync(Start("a"), OtherTabActivity());

        Assert.Equal(1, result.Heartbeats);
    }

    private static Task<ScriptResult> RunWithClipboardAsync(string clipboard, params object[] steps) =>
        RunAsync(clipboard, steps);

    private static Task<ScriptResult> RunAsync(params object[] steps) => RunAsync("ok", steps);

    private static async Task<ScriptResult> RunAsync(string clipboard, object[] steps)
    {
        var scenario = JsonSerializer.Serialize(new { formId = FormId, clipboard, steps });
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(HarnessPath);
        startInfo.ArgumentList.Add(ScriptPath);
        startInfo.ArgumentList.Add(scenario);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("node could not be started.");
        var output = await process.StandardOutput.ReadToEndAsync(Xunit.TestContext.Current.CancellationToken);
        var error = await process.StandardError.ReadToEndAsync(Xunit.TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, error);
        return JsonSerializer.Deserialize<ScriptResult>(output, ResultOptions)!;
    }

    private sealed record ScriptResult(int Submits, int Timers, List<string> Order, List<string> Writes, int Heartbeats);
}
