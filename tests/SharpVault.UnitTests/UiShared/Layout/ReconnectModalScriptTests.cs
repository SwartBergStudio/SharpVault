// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Text.Json;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Layout;

[Trait("Category", "Security")]
public sealed class ReconnectModalScriptTests
{
    private const string Secret = "hunter2-plaintext";

    private static readonly string ScriptPath = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Web", "Components", "Layout", "ReconnectModal.razor.js");
    private static readonly string HarnessPath = Path.Combine(RepositoryRoot.Path, "tests", "SharpVault.UnitTests", "UiShared", "Layout", "reconnect-modal.harness.mjs");
    private static readonly JsonSerializerOptions ResultOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Disconnect_HidesSensitiveValuesImmediatelyWithoutTouchingTheText()
    {
        var result = await RunAsync(["show"]);

        Assert.True(result.Hidden);
        Assert.True(result.ModalOpen);
        Assert.Equal([Secret, Secret, Secret], result.Texts);
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("resume-failed")]
    public async Task ReconnectFailed_RemovesTheTextFromTheDom(string failure)
    {
        var result = await RunAsync(["show", failure]);

        Assert.True(result.Hidden);
        Assert.All(result.Texts, text => Assert.Equal(string.Empty, text));
    }

    [Fact]
    public async Task ReconnectRejected_RemovesTheTextAndReloads()
    {
        var result = await RunAsync(["show", "rejected"]);

        Assert.All(result.Texts, text => Assert.Equal(string.Empty, text));
        Assert.Equal(1, result.Reloads);
    }

    [Fact]
    public async Task ReconnectSucceeded_ShowsTheServerRenderedValuesAgainWithoutReloading()
    {
        var result = await RunAsync(["show", "hide"]);

        Assert.False(result.Hidden);
        Assert.False(result.ModalOpen);
        Assert.Equal(0, result.Reloads);
        Assert.Equal([Secret, Secret, Secret], result.Texts);
    }

    [Fact]
    public async Task ReconnectSucceededAfterTheTextWasRemoved_ReloadsSoTheDomMatchesTheServer()
    {
        var result = await RunAsync(["show", "failed", "hide"]);

        Assert.Equal(1, result.Reloads);
        Assert.All(result.Texts, text => Assert.Equal(string.Empty, text));
    }

    [Fact]
    public async Task DisconnectedLongerThanThirtySeconds_RemovesTheTextWithoutWaitingForTheReconnectToFail()
    {
        var result = await RunAsync(["show", "advance:29999"]);
        var after = await RunAsync(["show", "advance:30000"]);

        Assert.Equal([Secret, Secret, Secret], result.Texts);
        Assert.All(after.Texts, text => Assert.Equal(string.Empty, text));
    }

    [Fact]
    public async Task ReconnectBeforeThirtySeconds_NeverRemovesTheText()
    {
        var result = await RunAsync(["show", "advance:20000", "hide", "advance:60000"]);

        Assert.Equal([Secret, Secret, Secret], result.Texts);
        Assert.Equal(0, result.Reloads);
    }

    [Fact]
    public async Task SessionPaused_HidesSensitiveValues()
    {
        var result = await RunAsync(["paused"]);

        Assert.True(result.Hidden);
    }

    private static async Task<ScriptResult> RunAsync(string[] states)
    {
        var elements = new[]
        {
            new { kind = "span", text = Secret },
            new { kind = "input", text = Secret },
            new { kind = "textarea", text = Secret },
        };
        var scenario = JsonSerializer.Serialize(new { states, elements, reconnect = "ok" });
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

    private sealed record ScriptResult(int Reloads, bool ModalOpen, bool Hidden, List<string> Texts);
}
