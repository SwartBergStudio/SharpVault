// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components;
using SharpVault.PasswordGeneration;
using SharpVault.UI.Shared.Components.Common;

namespace SharpVault.UI.Shared.Components.Vault;

public sealed partial class PasswordGeneratorDialog
{
    public const string CopyFailedText = "Copy failed.";

    private GeneratorMode _mode = GeneratorMode.Password;
    private PasswordOptions _password = PasswordOptions.Default;
    private PassphraseOptions _passphrase = PassphraseOptions.Default;
    private GeneratedSecret? _generated;
    private string? _error;
    private readonly StatusMessage _status = new();
    private bool _wasOpen;

    private enum GeneratorMode
    {
        Password,
        Passphrase,
    }

    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback<string> OnUse { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private string EntropyText =>
        Math.Round(_generated?.EntropyBits ?? 0, 1).ToString("F1", CultureInfo.InvariantCulture);

    private string StrengthText => PasswordGenerator.StrengthOf(_generated?.EntropyBits ?? 0) switch
    {
        PasswordStrength.VeryWeak => "Very weak",
        PasswordStrength.Weak => "Weak",
        PasswordStrength.Reasonable => "Reasonable",
        PasswordStrength.Strong => "Strong",
        _ => "Very strong",
    };

    protected override void OnInitialized() => Track(new GeneratedValueScrub(this));

    protected override void OnParametersSet()
    {
        if (Open && !_wasOpen)
        {
            Generate();
        }
        else if (!Open && _wasOpen)
        {
            Discard();
        }

        _wasOpen = Open;
    }

    private SvButtonVariant ModeVariant(GeneratorMode mode) =>
        _mode == mode ? SvButtonVariant.Primary : SvButtonVariant.Secondary;

    private void SelectMode(GeneratorMode mode)
    {
        _mode = mode;
        Generate();
    }

    private void ChangePassword(PasswordOptions options)
    {
        _password = options;
        Generate();
    }

    private void ChangePassphrase(PassphraseOptions options)
    {
        _passphrase = options;
        Generate();
    }

    private void Regenerate() => Generate();

    private void Generate()
    {
        _status.Clear();
        _generated = null;
        var problems = _mode == GeneratorMode.Password
            ? PasswordGenerator.Validate(_password)
            : PasswordGenerator.Validate(_passphrase);
        _error = problems.Count > 0 ? problems[0] : null;
        if (_error is not null)
        {
            return;
        }

        try
        {
            _generated = _mode == GeneratorMode.Password
                ? PasswordGenerator.GeneratePassword(_password)
                : PasswordGenerator.GeneratePassphrase(_passphrase);
        }
        catch (InvalidOperationException exception)
        {
            _error = exception.Message;
        }
    }

    private void Discard()
    {
        _generated = null;
        _error = null;
        _status.Clear();
    }

    private async Task CopyAsync()
    {
        if (_generated is null)
        {
            return;
        }

        var copied = await Clipboard.CopyAsync(_generated.Value, Cts.Token);
        _status.Show(copied ? SecretStatusText.Copied(Settings) : CopyFailedText, AlertKind.Info, expires: true);
    }

    private async Task UseAsync()
    {
        if (_generated is null)
        {
            return;
        }

        var value = _generated.Value;
        Discard();
        await OnUse.InvokeAsync(value);
    }

    private async Task HandleCancel()
    {
        Discard();
        await OnCancel.InvokeAsync();
    }

    private sealed class GeneratedValueScrub(PasswordGeneratorDialog dialog) : IDisposable
    {
        public void Dispose() => dialog._generated = null;
    }
}
