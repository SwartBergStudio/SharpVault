// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.OneTimeLinks;

namespace SharpVault.UI.Shared.Components.OneTimeLinks;

public sealed partial class OneTimeLinkCreateView
{
    public const string PassphraseTooShortText = "The passphrase must be at least 8 characters, or leave it empty.";
    public const string UnsupportedText = "This browser cannot create encrypted links. Use a current browser over HTTPS.";
    public const string CopiedText = "The link was copied.";
    public const string CopyFailedText = "The link could not be copied. Select it and copy it by hand.";

    private const string PassphraseInputId = "otl-passphrase";
    private const string LinkOutputInputId = "otl-link-output";

    private static readonly (int Minutes, string Label)[] LifetimeChoices =
    [
        (60, "1 hour"),
        (1440, "24 hours"),
        (4320, "3 days"),
        (10080, "7 days"),
    ];

    private readonly StatusMessage _status = new();
    private List<SvSelectOption> _lifetimeOptions = [];
    private string? _lifetime;
    private bool _published;

    [Parameter, EditorRequired]
    public Guid OwnerId { get; set; }

    [Parameter, EditorRequired]
    public Guid SecretId { get; set; }

    protected override async Task<ApiResponse<OneTimeLinkSettingsDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var response = await Links.GetSettingsAsync(cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            PrepareLifetimes(response.Value);
        }

        return response;
    }

    private void PrepareLifetimes(OneTimeLinkSettingsDto settings)
    {
        _lifetimeOptions = LifetimeChoices
            .Where(choice => choice.Minutes <= settings.MaxLifetimeMinutes)
            .Select(choice => new SvSelectOption(choice.Minutes.ToString(CultureInfo.InvariantCulture), choice.Label))
            .ToList();
        var defaultValue = settings.DefaultLifetimeMinutes.ToString(CultureInfo.InvariantCulture);
        _lifetime = _lifetimeOptions.Any(option => option.Value == defaultValue)
            ? defaultValue
            : _lifetimeOptions.Count > 0 ? _lifetimeOptions[^1].Value : null;
    }

    private Task HandleLifetimeChangedAsync(string value)
    {
        _lifetime = value;
        return Task.CompletedTask;
    }

    private Task CreateLinkAsync() => RunCommandAsync(CreateCoreAsync, ShowFailed);

    private async Task CreateCoreAsync(CancellationToken cancellationToken)
    {
        _published = false;
        _status.Clear();
        var snapshot = await Links.GetSnapshotAsync(OwnerId, SecretId, cancellationToken);
        if (snapshot.Status != ApiCallStatus.Success || snapshot.Value is null)
        {
            ShowFailure(snapshot);
            return;
        }

        var seal = await Crypto.SealAsync(JsonSerializer.Serialize(snapshot.Value, JsonSerializerOptions.Web), PassphraseInputId, cancellationToken);
        if (seal.Status != OneTimeLinkSealStatus.Sealed || seal.Handle is null || seal.Payload is null)
        {
            _status.Show(SealFailureText(seal.Status), AlertKind.Danger);
            return;
        }

        var created = await Links.CreateAsync(
            OwnerId,
            SecretId,
            new CreateOneTimeLinkRequestDto { Payload = Convert.ToBase64String(seal.Payload), LifetimeMinutes = SelectedLifetime() },
            cancellationToken);
        if (created.Status != ApiCallStatus.Success || created.Value is null)
        {
            await Crypto.DiscardAsync(seal.Handle, cancellationToken);
            ShowFailure(created);
            return;
        }

        await Crypto.PublishAsync(seal.Handle, created.Value.Id, BaseUri.Value, LinkOutputInputId, cancellationToken);
        _published = true;
    }

    private Task CopyLinkCommandAsync() => RunCommandAsync(async cancellationToken =>
    {
        var copied = await Crypto.CopyLinkAsync(LinkOutputInputId, cancellationToken);
        _status.Show(copied ? CopiedText : CopyFailedText, copied ? AlertKind.Success : AlertKind.Danger);
    });

    private static string SealFailureText(OneTimeLinkSealStatus status) => status switch
    {
        OneTimeLinkSealStatus.PassphraseTooShort => PassphraseTooShortText,
        _ => UnsupportedText,
    };

    private int? SelectedLifetime() =>
        int.TryParse(_lifetime, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ? minutes : null;

    private void ShowFailure<T>(ApiResponse<T> response)
    {
        if (TryHandleSessionStatus(response.Status))
        {
            return;
        }

        _status.Show(VaultStatusText.For(response), AlertKind.Danger);
    }

    private void ShowFailed() => _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
}
