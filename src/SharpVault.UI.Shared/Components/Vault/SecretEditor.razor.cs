// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UI.Shared.Components.Vault;

public sealed partial class SecretEditor
{
    public const string NameRequiredText = "Enter a name.";
    public const string DamagedText = "This item could not be read, so it cannot be edited. You can still delete it from its page.";
    public const string ConflictText = "This item was changed elsewhere. Your edits are still here and nothing was saved. Reload to get the latest version, which discards your edits.";

    private UpdateSecretRequestDto _model = new();
    private IReadOnlyList<FolderDto> _folders = [];
    private string _tagsText = string.Empty;
    private string _concurrencyToken = string.Empty;
    private Guid? _folderId;
    private string? _formError;
    private bool _conflict;
    private bool _foldersUnavailable;

    [Parameter]
    public Guid? SecretId { get; set; }

    [Parameter]
    public string? FolderId { get; set; }

    [Inject]
    private IVaultSecretsApiClient Secrets { get; set; } = default!;

    [Inject]
    private IVaultFoldersApiClient FoldersClient { get; set; } = default!;

    [Inject]
    private NavigationManager Navigator { get; set; } = default!;

    private bool IsNew => SecretId is null;

    private Guid? RequestedFolderId => Guid.TryParse(FolderId, out var id) ? id : null;

    private string CancelRoute => (SecretId, RequestedFolderId) switch
    {
        ({ } id, _) => VaultRoutes.Secret(id),
        (null, { } folder) => VaultRoutes.Folder(folder),
        _ => VaultRoutes.Vault,
    };

    protected override void OnInitialized() => Track(new ModelScrub(this));

    protected override async Task<ApiResponse<SecretDto>> LoadAsync(CancellationToken cancellationToken)
    {
        _conflict = false;
        _formError = null;
        return SecretId is { } id ? await LoadExistingAsync(id, cancellationToken) : await LoadNewAsync(cancellationToken);
    }

    private async Task<ApiResponse<SecretDto>> LoadExistingAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await Secrets.GetSecretAsync(id, cancellationToken);
        if (response.Status != ApiCallStatus.Success || response.Value is not { } secret)
        {
            return response;
        }

        _concurrencyToken = secret.ConcurrencyToken;
        _tagsText = SecretRequestMapper.JoinTags(secret.Tags);
        _model = new UpdateSecretRequestDto
        {
            Type = secret.Type,
            Name = secret.Name ?? string.Empty,
            Username = secret.Username,
            Url = secret.Url,
        };
        return response;
    }

    private async Task<ApiResponse<SecretDto>> LoadNewAsync(CancellationToken cancellationToken)
    {
        var folders = await FoldersClient.ListFoldersAsync(cancellationToken);
        if (folders.Status.FailsTheWholeView())
        {
            return new ApiResponse<SecretDto>(folders.Status, null);
        }

        _foldersUnavailable = folders.Status != ApiCallStatus.Success || folders.Value is null;
        _folders = _foldersUnavailable ? [] : folders.Value!.Items;
        _folderId = RequestedFolderId is { } id && _folders.Any(folder => folder.Id == id) ? id : null;
        _tagsText = string.Empty;
        _model = new UpdateSecretRequestDto { Type = SecretTypeDto.Login };
        return ApiResponse<SecretDto>.Success(new SecretDto { Type = SecretTypeDto.Login });
    }

    private void ApplyGeneratedValue(string value)
    {
        _model.ReplaceValue = true;
        _model.Value = value;
    }

    private Task ReplaceNotesChangedAsync(bool replace)
    {
        if (!replace)
        {
            _model.ReplaceNotes = false;
            _model.Notes = null;
            return Task.CompletedTask;
        }

        if (SecretId is not { } id)
        {
            return Task.CompletedTask;
        }

        if (Value is { HasNotes: false })
        {
            _model.ReplaceNotes = true;
            return Task.CompletedTask;
        }

        _formError = null;
        var request = new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.View };
        return RunApiCommandAsync(
            token => Secrets.RevealSecretAsync(id, request, token),
            ApplyNotes,
            ShowFailed);
    }

    private Task ApplyNotes(ApiResponse<RevealedValueDto> response)
    {
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _model.Notes = response.Value.Value;
            _model.ReplaceNotes = true;
        }
        else
        {
            _formError = VaultStatusText.For(response);
        }

        return Task.CompletedTask;
    }

    private void ShowFailed() => _formError = VaultStatusText.FailedText;

    private Task SaveAsync()
    {
        _formError = null;
        _conflict = false;
        if (string.IsNullOrWhiteSpace(_model.Name))
        {
            _formError = NameRequiredText;
            return Task.CompletedTask;
        }

        return RunApiCommandAsync(
            SaveCoreAsync,
            response =>
            {
                Apply(response);
                return Task.CompletedTask;
            },
            ShowFailed);
    }

    private Task<ApiResponse<SecretDto>> SaveCoreAsync(CancellationToken cancellationToken) =>
        SecretId is { } id
            ? Secrets.UpdateSecretAsync(id, _concurrencyToken, SecretRequestMapper.ForUpdate(_model, _tagsText), cancellationToken)
            : Secrets.CreateSecretAsync(SecretRequestMapper.ForCreate(_model, _tagsText, _folderId), cancellationToken);

    private void Apply(ApiResponse<SecretDto> response)
    {
        if (response.Status == ApiCallStatus.Success && response.Value is { } saved)
        {
            Navigator.NavigateTo(VaultRoutes.Secret(saved.Id));
            return;
        }

        if (response.Status == ApiCallStatus.Conflict)
        {
            _conflict = true;
            return;
        }

        _formError = VaultStatusText.For(response);
    }

    private sealed class ModelScrub(SecretEditor editor) : IDisposable
    {
        public void Dispose()
        {
            editor._model.Value = null;
            editor._model.Notes = null;
        }
    }
}
