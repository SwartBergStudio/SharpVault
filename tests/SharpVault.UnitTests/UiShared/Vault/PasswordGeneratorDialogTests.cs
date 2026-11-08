// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor;
using NSubstitute;
using SharpVault.PasswordGeneration;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class PasswordGeneratorDialogTests : VaultTestContext
{
    private readonly IRenderedComponent<MudDialogProvider> _provider;
    private readonly List<string> _used = [];
    private int _cancelled;

    public PasswordGeneratorDialogTests()
    {
        _provider = RenderDialogProviders();
    }

    private FakeTimeProvider Time => Services.GetRequiredService<FakeTimeProvider>();

    private static GeneratedSecret? HeldSecret(IRenderedComponent<PasswordGeneratorDialog> cut) =>
        (GeneratedSecret?)typeof(PasswordGeneratorDialog).GetField("_generated", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);

    private IRenderedComponent<PasswordGeneratorDialog> RenderOpen() =>
        Render<PasswordGeneratorDialog>(parameters => parameters
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.OnUse, value => _used.Add(value))
            .Add(dialog => dialog.OnCancel, () => _cancelled++));

    private string Output => _provider.Find("#generator-output").TextContent;

    private void SetLength(int length) => _provider.Find("#generator-length").Input(length);

    private void SetSet(string id, bool on) => _provider.Find("#" + id).Change(on);

    private void AssertEveryClientUntouched()
    {
        Assert.Empty(SecretsClient.ReceivedCalls());
        Assert.Empty(HistoryClient.ReceivedCalls());
        Assert.Empty(FoldersClient.ReceivedCalls());
        Assert.Empty(RecycleBinClient.ReceivedCalls());
    }

    [Fact]
    public void Open_ShowsAPasswordAndEnablesUseAndCopy()
    {
        RenderOpen();

        Assert.Equal(PasswordOptions.Default.Length, Output.Length);
        Assert.Empty(_provider.FindAll("#generator-error"));
        Assert.False(_provider.Find("#generator-use").HasAttribute("disabled"));
        Assert.False(_provider.Find("#generator-copy").HasAttribute("disabled"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Open_GeneratedValue_IsMarkedSensitive()
    {
        RenderOpen();

        Assert.Equal("true", _provider.Find("#generator-output").GetAttribute("data-sv-sensitive"));
    }

    [Fact]
    public void Open_NotOpen_GeneratesNothing()
    {
        Render<PasswordGeneratorDialog>(parameters => parameters.Add(dialog => dialog.Open, false));

        Assert.Empty(_provider.FindAll("#generator-output"));
    }

    [Theory]
    [InlineData("generator-symbols")]
    [InlineData("generator-exclude-ambiguous")]
    public void ChangeOption_Regenerates(string id)
    {
        RenderOpen();
        SetLength(64);
        var before = Output;
        var wasOn = _provider.Find("#" + id).HasAttribute("checked");

        SetSet(id, !wasOn);

        Assert.NotEqual(before, Output);
        Assert.Equal(64, Output.Length);
    }

    [Fact]
    public void Regenerate_Click_ProducesANewValue()
    {
        RenderOpen();
        var before = Output;

        _provider.Find("#generator-regenerate").Click();

        Assert.NotEqual(before, Output);
    }

    [Fact]
    public void UncheckAllSets_ShowsErrorAndDisablesUseAndCopy()
    {
        var cut = RenderOpen();

        SetSet("generator-lowercase", false);
        SetSet("generator-uppercase", false);
        SetSet("generator-digits", false);
        SetSet("generator-symbols", false);

        Assert.Equal(GeneratorMessages.NoCharacterSet, _provider.Find("#generator-error").TextContent.Replace("Error: ", string.Empty, StringComparison.Ordinal).Trim());
        Assert.Empty(_provider.FindAll("#generator-output"));
        Assert.True(_provider.Find("#generator-use").HasAttribute("disabled"));
        Assert.True(_provider.Find("#generator-copy").HasAttribute("disabled"));
        Assert.True(_provider.Find("#generator-regenerate").HasAttribute("disabled"));
        Assert.Null(HeldSecret(cut));
    }

    [Theory]
    [InlineData(GeneratorLimits.MinLength - 1)]
    [InlineData(GeneratorLimits.MaxLength + 1)]
    public void Length_OutOfRange_ShowsErrorAndDisablesUseAndCopy(int length)
    {
        RenderOpen();

        SetLength(length);

        Assert.Contains(GeneratorMessages.LengthOutOfRange, _provider.Find("#generator-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(_provider.FindAll("#generator-output"));
        Assert.True(_provider.Find("#generator-use").HasAttribute("disabled"));
        Assert.True(_provider.Find("#generator-copy").HasAttribute("disabled"));
    }

    [Fact]
    public void InvalidOptions_UseAndCopy_DoNothing()
    {
        RenderOpen();
        SetLength(GeneratorLimits.MinLength - 1);

        _provider.Find("#generator-use").Click();
        _provider.Find("#generator-copy").Click();

        Assert.Empty(_used);
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    public void PassphraseMode_WordCountOutOfRange_ShowsErrorAndDisablesUseAndCopy()
    {
        RenderOpen();
        _provider.Find("#generator-mode-passphrase").Click();

        _provider.Find("#generator-word-count").Input(GeneratorLimits.MaxWords + 1);

        Assert.Contains(GeneratorMessages.WordCountOutOfRange, _provider.Find("#generator-error").TextContent, StringComparison.Ordinal);
        Assert.True(_provider.Find("#generator-use").HasAttribute("disabled"));
        Assert.True(_provider.Find("#generator-copy").HasAttribute("disabled"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Copy_WritesClipboardWithoutApiCall()
    {
        RenderOpen();
        var shown = Output;

        _provider.Find("#generator-copy").Click();

        Assert.Equal([shown], Clipboard.Writes);
        AssertEveryClientUntouched();
        Assert.Contains(SecretStatusText.Copied(Settings), _provider.Find("#generator-status").TextContent, StringComparison.Ordinal);
        Assert.Empty(_used);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Copy_AfterTheClipboardDelay_ClearsTheClipboard()
    {
        RenderOpen();
        await _provider.Find("#generator-copy").ClickAsync();
        Assert.Single(Clipboard.Writes);

        Time.Advance(Settings.ClipboardClearDelay);
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }

        _provider.WaitForAssertion(() => Assert.Equal(1, Clipboard.ClearCalls));
        Assert.Equal(string.Empty, Clipboard.Current);
    }

    [Fact]
    public void Copy_ClipboardFails_ShowsCopyFailed()
    {
        Clipboard.FailWritesWithJavaScriptError("blocked");
        RenderOpen();

        _provider.Find("#generator-copy").Click();

        Assert.Contains(PasswordGeneratorDialog.CopyFailedText, _provider.Find("#generator-status").TextContent, StringComparison.Ordinal);
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyAndUse_NeverWriteTheGeneratedValueToTheLogs()
    {
        RenderOpen();
        var shown = Output;
        _provider.Find("#generator-copy").Click();
        _provider.Find("#generator-use").Click();

        Assert.DoesNotContain(shown, LogText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Use_RaisesCallbackWithValueAndClearsTheHeldValue()
    {
        var cut = RenderOpen();
        var shown = Output;

        _provider.Find("#generator-use").Click();

        Assert.Equal([shown], _used);
        Assert.Null(HeldSecret(cut));
        Assert.Empty(_provider.FindAll("#generator-output"));
        Assert.DoesNotContain(shown, _provider.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Cancel_RaisesCallbackAndClearsTheHeldValue()
    {
        var cut = RenderOpen();
        var shown = Output;

        _provider.Find("#generator-cancel").Click();

        Assert.Equal(1, _cancelled);
        Assert.Null(HeldSecret(cut));
        Assert.DoesNotContain(shown, _provider.Markup, StringComparison.Ordinal);
        Assert.Empty(_used);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Close_ByParent_ClearsTheHeldValueAndReopeningGeneratesAFreshOne()
    {
        var cut = RenderOpen();
        var shown = Output;

        cut.Render(parameters => parameters.Add(dialog => dialog.Open, false));

        Assert.Null(HeldSecret(cut));
        Assert.DoesNotContain(shown, _provider.Markup, StringComparison.Ordinal);
        cut.Render(parameters => parameters.Add(dialog => dialog.Open, true));
        Assert.NotNull(HeldSecret(cut));
        Assert.NotEqual(shown, Output);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Dispose_ClearsTheHeldValue()
    {
        var cut = RenderOpen();
        Assert.NotNull(HeldSecret(cut));

        await cut.Instance.DisposeAsync();

        Assert.Null(HeldSecret(cut));
    }
}
