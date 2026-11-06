// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

[Trait("Category", "Security")]
public sealed class RevealableFieldTests : VaultTestContext
{
    private const string Prefix = "secret-value";
    private const string Secret = "hunter2-revealed-secret";

    private readonly List<RevealIntentDto> _intents = [];
    private readonly List<ApiCallStatus> _failures = [];
    private int _copied;
    private Func<RevealIntentDto, CancellationToken, Task<ApiResponse<RevealedValueDto>>> _reveal;

    public RevealableFieldTests()
    {
        _reveal = (_, _) => Task.FromResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = Secret }));
    }

    private FakeTimeProvider Time => Services.GetRequiredService<FakeTimeProvider>();

    private static string? HeldText(RevealableField field)
    {
        var held = typeof(RevealableField).GetField("_held", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(field)!;
        return (string?)held.GetType().GetProperty("Text")!.GetValue(held);
    }

    private IRenderedComponent<RevealableField> RenderField() =>
        Render<RevealableField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Label, "Password")
            .Add(field => field.Reveal, (intent, token) =>
            {
                _intents.Add(intent);
                return _reveal(intent, token);
            })
            .Add(field => field.OnFailure, status => _failures.Add(status))
            .Add(field => field.OnCopied, () => _copied++));

    private static async Task SettleAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }
    }

    [Fact]
    public void Render_Initially_ShowsTheMaskAndNeverCallsReveal()
    {
        var cut = RenderField();

        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(_intents);
        Assert.NotNull(cut.Find($"#{Prefix}-reveal"));
        Assert.Empty(cut.FindAll($"#{Prefix}-hide"));
        Assert.NotNull(cut.Find($"#{Prefix}-copy"));
    }

    [Fact]
    public void Reveal_Click_CallsRevealWithViewIntentAndShowsTheValue()
    {
        var cut = RenderField();

        cut.Find($"#{Prefix}-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(Secret, cut.Find($"#{Prefix}").TextContent, StringComparison.Ordinal));
        Assert.Equal([RevealIntentDto.View], _intents);
        Assert.Empty(cut.FindAll($"#{Prefix}-reveal"));
        Assert.NotNull(cut.Find($"#{Prefix}-hide"));
    }

    [Fact]
    public void Print_StateAttributeMarksRevealed()
    {
        var cut = RenderField();
        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));

        cut.Find($"#{Prefix}-reveal").Click();

        cut.WaitForAssertion(() => Assert.Equal("revealed", cut.Find($"#{Prefix}-frame").GetAttribute("data-state")));
    }

    [Fact]
    public void Reveal_ValueWithMarkup_IsRenderedEncoded()
    {
        _reveal = (_, _) => Task.FromResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = "<img src=x onerror=alert(1)>" }));
        var cut = RenderField();

        cut.Find($"#{Prefix}-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains("&lt;img", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("img"));
    }

    [Fact]
    public void Hide_Click_RemovesTheValueFromTheMarkup()
    {
        var cut = RenderField();
        cut.Find($"#{Prefix}-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal));

        cut.Find($"#{Prefix}-hide").Click();

        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
        Assert.Null(HeldText(cut.Instance));
    }

    [Fact]
    public async Task RevealableField_AfterAutoHideDelay_HidesValue()
    {
        var cut = RenderField();
        cut.Find($"#{Prefix}-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal));

        Time.Advance(Settings.RevealAutoHideDelay - TimeSpan.FromMilliseconds(1));
        await SettleAsync();
        Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal);

        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal));
        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
        Assert.Null(HeldText(cut.Instance));
    }

    [Fact]
    public async Task Reveal_HideThenRevealAgain_RestartsTheAutoHideWindow()
    {
        var cut = RenderField();
        cut.Find($"#{Prefix}-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal));
        Time.Advance(TimeSpan.FromSeconds(20));
        await SettleAsync();
        cut.Find($"#{Prefix}-hide").Click();
        cut.Find($"#{Prefix}-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal));

        Time.Advance(TimeSpan.FromSeconds(20));
        await SettleAsync();

        Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal);
        Time.Advance(TimeSpan.FromSeconds(10));
        cut.WaitForAssertion(() => Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RevealableField_Dispose_DropsRevealedValue()
    {
        var cut = RenderField();
        cut.Find($"#{Prefix}-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal));
        Assert.Equal(Secret, HeldText(cut.Instance));

        await cut.Instance.DisposeAsync();

        Assert.Null(HeldText(cut.Instance));
    }

    [Fact]
    public async Task Dispose_WhileAutoHideIsWaiting_CancelsItWithoutError()
    {
        var cut = RenderField();
        cut.Find($"#{Prefix}-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal));

        await cut.Instance.DisposeAsync();
        Time.Advance(Settings.RevealAutoHideDelay * 2);
        await SettleAsync();

        Assert.Empty(_failures);
    }

    [Fact]
    public void Copy_Click_CallsRevealWithCopyIntentAndWritesTheClipboardWithoutRendering()
    {
        var cut = RenderField();

        cut.Find($"#{Prefix}-copy").Click();

        cut.WaitForAssertion(() => Assert.Equal([Secret], Clipboard.Writes));
        Assert.Equal([RevealIntentDto.Copy], _intents);
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Equal(1, _copied);
        Assert.Empty(_failures);
        Assert.Null(HeldText(cut.Instance));
    }

    [Fact]
    public async Task Copy_AfterTheClipboardDelay_ClearsTheClipboard()
    {
        var cut = RenderField();
        cut.Find($"#{Prefix}-copy").Click();
        cut.WaitForAssertion(() => Assert.Equal([Secret], Clipboard.Writes));

        Time.Advance(Settings.ClipboardClearDelay);

        cut.WaitForAssertion(() => Assert.Equal(1, Clipboard.ClearCalls));
        await SettleAsync();
        Assert.Equal(string.Empty, Clipboard.Current);
    }

    [Fact]
    public void Reveal_SucceedsWithNoValue_ReportsFailedNotSuccess()
    {
        _reveal = (_, _) => Task.FromResult(ApiResponse<RevealedValueDto>.Success(null!));
        var cut = RenderField();

        cut.Find($"#{Prefix}-reveal").Click();

        cut.WaitForAssertion(() => Assert.Equal([ApiCallStatus.Failed], _failures));
        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
    }

    [Fact]
    public void Copy_ClipboardWriteFails_RaisesFailureAndNotCopied()
    {
        Clipboard.FailWritesWithJavaScriptError("blocked");
        var cut = RenderField();

        cut.Find($"#{Prefix}-copy").Click();

        cut.WaitForAssertion(() => Assert.Equal([ApiCallStatus.Failed], _failures));
        Assert.Equal(0, _copied);
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ApiCallStatus.NotFound)]
    [InlineData(ApiCallStatus.RateLimited)]
    [InlineData(ApiCallStatus.Conflict)]
    [InlineData(ApiCallStatus.SignInRequired)]
    [InlineData(ApiCallStatus.AccessDenied)]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.ConfigurationMismatch)]
    public void Failure_RaisesOnFailureWithStatus(ApiCallStatus status)
    {
        _reveal = (_, _) => Task.FromResult(new ApiResponse<RevealedValueDto>(status, null));
        var cut = RenderField();

        cut.Find($"#{Prefix}-reveal").Click();

        cut.WaitForAssertion(() => Assert.Equal([status], _failures));
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    public void Failure_OnCopy_WritesNothingToTheClipboard()
    {
        _reveal = (_, _) => Task.FromResult(ApiResponse<RevealedValueDto>.RateLimited());
        var cut = RenderField();

        cut.Find($"#{Prefix}-copy").Click();

        cut.WaitForAssertion(() => Assert.Equal([ApiCallStatus.RateLimited], _failures));
        Assert.Empty(Clipboard.Writes);
        Assert.Equal(0, _copied);
    }

    [Fact]
    public void Reveal_WhileARequestIsInFlight_DoesNotCallTheApiAgain()
    {
        var gate = new TaskCompletionSource<ApiResponse<RevealedValueDto>>();
        _reveal = (_, _) => gate.Task;
        var cut = RenderField();

        cut.Find($"#{Prefix}-reveal").Click();
        cut.Find($"#{Prefix}-copy").Click();
        cut.Find($"#{Prefix}-reveal").Click();

        Assert.Single(_intents);
        gate.SetResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = Secret }));
        cut.WaitForAssertion(() => Assert.Contains(Secret, cut.Markup, StringComparison.Ordinal));
        Assert.Single(_intents);
    }

    [Fact]
    public async Task Dispose_WhileRevealIsInFlight_IgnoresTheLateValueAndRaisesNoFailure()
    {
        var gate = new TaskCompletionSource<ApiResponse<RevealedValueDto>>();
        CancellationToken observed = default;
        _reveal = async (_, token) =>
        {
            observed = token;
            await gate.Task;
            token.ThrowIfCancellationRequested();
            return ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = Secret });
        };
        var cut = RenderField();
        cut.Find($"#{Prefix}-reveal").Click();

        await cut.Instance.DisposeAsync();
        gate.SetResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = Secret }));
        await SettleAsync();

        Assert.True(observed.IsCancellationRequested);
        Assert.Empty(_failures);
        Assert.Null(HeldText(cut.Instance));
        Assert.DoesNotContain(Secret, cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void InvalidIdPrefix_Throws(string prefix)
    {
        ThrownExceptions.ArgumentFrom(() => Render<RevealableField>(parameters => parameters
            .Add(field => field.IdPrefix, prefix)
            .Add(field => field.Label, "Password")
            .Add(field => field.Reveal, (_, _) => Task.FromResult(ApiResponse<RevealedValueDto>.Failed()))));
    }

}
