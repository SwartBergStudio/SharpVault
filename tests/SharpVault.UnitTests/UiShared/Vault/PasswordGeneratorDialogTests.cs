// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor;
using NSubstitute;
using SharpVault.PasswordGeneration;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed partial class PasswordGeneratorDialogTests : VaultTestContext
{
    private readonly IRenderedComponent<MudDialogProvider> _provider;
    private readonly List<string> _used = [];
    private int _cancelled;

    public PasswordGeneratorDialogTests()
    {
        _provider = RenderDialogProviders();
    }

    private FakeTimeProvider Time => Services.GetRequiredService<FakeTimeProvider>();

    [GeneratedRegex("^Entropy: (?<bits>\\d+\\.\\d) bits$")]
    private static partial Regex EntropyPattern();

    private static GeneratedSecret? HeldSecret(IRenderedComponent<PasswordGeneratorDialog> cut) =>
        (GeneratedSecret?)typeof(PasswordGeneratorDialog).GetField("_generated", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(cut.Instance);

    private static string ExpectedStrengthText(double bits) => PasswordGenerator.StrengthOf(bits) switch
    {
        PasswordStrength.VeryWeak => "Very weak",
        PasswordStrength.Weak => "Weak",
        PasswordStrength.Reasonable => "Reasonable",
        PasswordStrength.Strong => "Strong",
        _ => "Very strong",
    };

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
    public void Open_ShowsPasswordWithEntropyAndStrength()
    {
        RenderOpen();

        Assert.Equal(PasswordOptions.Default.Length, Output.Length);
        var entropy = EntropyPattern().Match(_provider.Find("#generator-entropy").TextContent);
        Assert.True(entropy.Success, _provider.Find("#generator-entropy").TextContent);
        var bits = double.Parse(entropy.Groups["bits"].Value, CultureInfo.InvariantCulture);
        Assert.Equal("Strength: " + ExpectedStrengthText(bits), _provider.Find("#generator-strength").TextContent);
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
    [InlineData("generator-lowercase,generator-uppercase,generator-symbols", 8, "26.6", "Very weak")]
    [InlineData("generator-uppercase,generator-digits,generator-symbols", 8, "37.6", "Very weak")]
    [InlineData("generator-symbols", 8, "47.6", "Weak")]
    [InlineData("generator-symbols", 12, "71.5", "Reasonable")]
    [InlineData("generator-symbols", 16, "95.3", "Strong")]
    [InlineData("generator-symbols", 22, "131.0", "Very strong")]
    public void Entropy_ForKnownOptions_ShowsOneDecimalAndTheStrengthText(string unticked, int length, string bits, string strength)
    {
        RenderOpen();
        SetSet("generator-require-every-set", false);
        foreach (var id in unticked.Split(','))
        {
            SetSet(id, false);
        }

        SetLength(length);

        Assert.Equal($"Entropy: {bits} bits", _provider.Find("#generator-entropy").TextContent);
        Assert.Equal($"Strength: {strength}", _provider.Find("#generator-strength").TextContent);
        Assert.Equal(length, Output.Length);
    }

    [Fact]
    public void ChangeLength_Regenerates()
    {
        RenderOpen();
        var before = Output;

        SetLength(8);
        var shorter = Output;
        SetLength(64);
        var longer = Output;

        Assert.Equal(8, shorter.Length);
        Assert.Equal(64, longer.Length);
        Assert.NotEqual(before, shorter);
        Assert.NotEqual(shorter, longer);
        Assert.Equal("Length: 64", _provider.Find("#generator-length-label").TextContent);
    }

    [Theory]
    [InlineData("generator-lowercase")]
    [InlineData("generator-uppercase")]
    [InlineData("generator-digits")]
    [InlineData("generator-symbols")]
    [InlineData("generator-exclude-ambiguous")]
    [InlineData("generator-require-every-set")]
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
    public void ChangeOption_OnlyDigits_OutputIsDigits()
    {
        RenderOpen();

        SetSet("generator-lowercase", false);
        SetSet("generator-uppercase", false);
        SetSet("generator-symbols", false);

        Assert.Matches("^[0-9]{20}$", Output);
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
        Assert.Empty(_provider.FindAll("#generator-entropy"));
        Assert.Empty(_provider.FindAll("#generator-strength"));
        Assert.True(_provider.Find("#generator-use").HasAttribute("disabled"));
        Assert.True(_provider.Find("#generator-copy").HasAttribute("disabled"));
        Assert.True(_provider.Find("#generator-regenerate").HasAttribute("disabled"));
        Assert.Null(HeldSecret(cut));
    }

    [Fact]
    public void UncheckAllSets_ThenCheckOne_RecoversWithAValue()
    {
        RenderOpen();
        foreach (var id in new[] { "generator-lowercase", "generator-uppercase", "generator-digits", "generator-symbols" })
        {
            SetSet(id, false);
        }

        SetSet("generator-digits", true);

        Assert.Empty(_provider.FindAll("#generator-error"));
        Assert.Matches("^[0-9]{20}$", Output);
        Assert.False(_provider.Find("#generator-use").HasAttribute("disabled"));
        Assert.False(_provider.Find("#generator-copy").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(GeneratorLimits.MinLength - 1)]
    [InlineData(GeneratorLimits.MaxLength + 1)]
    [InlineData(0)]
    [InlineData(-5)]
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
    public void PassphraseMode_ShowsWords()
    {
        RenderOpen();

        _provider.Find("#generator-mode-passphrase").Click();
        _provider.Find("#generator-separator").Change("Space");

        var words = Output.Split(' ');
        Assert.Equal(PassphraseOptions.Default.WordCount, words.Length);
        Assert.All(words, word => Assert.Matches("^[a-z-]+$", word));
        var expectedBits = Math.Round(EntropyCalculator.PassphraseBits(6, false), 1);
        Assert.Equal($"Entropy: {expectedBits.ToString("F1", CultureInfo.InvariantCulture)} bits", _provider.Find("#generator-entropy").TextContent);
        Assert.Equal("Strength: " + ExpectedStrengthText(expectedBits), _provider.Find("#generator-strength").TextContent);
        Assert.Empty(_provider.FindAll("#generator-length"));
    }

    [Fact]
    public void PassphraseMode_OptionChanges_Regenerate()
    {
        RenderOpen();
        _provider.Find("#generator-mode-passphrase").Click();
        _provider.Find("#generator-separator").Change("Space");
        var before = Output;

        _provider.Find("#generator-word-count").Input(10);
        var tenWords = Output;
        _provider.Find("#generator-capitalize").Change(true);
        var capitalised = Output;
        _provider.Find("#generator-include-number").Change(true);
        var withNumber = Output;

        Assert.NotEqual(before, tenWords);
        Assert.Equal(10, tenWords.Split(' ').Length);
        Assert.All(capitalised.Split(' '), word => Assert.True(char.IsUpper(word[0]), word));
        Assert.Matches("[0-9]", withNumber);
        Assert.Equal(10, withNumber.Split(' ').Length);
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
    public void SwitchingMode_Regenerates()
    {
        RenderOpen();
        var password = Output;

        _provider.Find("#generator-mode-passphrase").Click();
        var passphrase = Output;
        _provider.Find("#generator-mode-password").Click();

        Assert.NotEqual(password, passphrase);
        Assert.Equal(PasswordOptions.Default.Length, Output.Length);
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
    public async Task Copy_AfterTheClipboardDelay_RemovesTheCopiedStatus()
    {
        RenderOpen();
        await _provider.Find("#generator-copy").ClickAsync();
        Assert.NotEmpty(_provider.FindAll("#generator-status"));

        Time.Advance(Settings.ClipboardClearDelay);

        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#generator-status")));
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

    [Fact]
    public async Task Open_Status_IsClearedWhenOptionsChange()
    {
        RenderOpen();
        await _provider.Find("#generator-copy").ClickAsync();
        Assert.NotEmpty(_provider.FindAll("#generator-status"));

        SetLength(30);

        Assert.Empty(_provider.FindAll("#generator-status"));
    }
}
