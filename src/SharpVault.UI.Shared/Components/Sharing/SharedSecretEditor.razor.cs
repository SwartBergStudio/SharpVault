// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UI.Shared.Components.Sharing;

public sealed partial class SharedSecretEditor
{
    public const string ViewOnlyText = "You can view and copy this item but not change it.";

    private UpdateSecretRequestDto _model = new();
    private string _tagsText = string.Empty;
    private string _concurrencyToken = string.Empty;
    private string? _formError;
    private bool _conflict;
    private bool _accessEnded;

    [Parameter, EditorRequired]
    public Guid OwnerId { get; set; }

    [Parameter, EditorRequired]
    public Guid SecretId { get; set; }

    [Inject]
    private ISharedItemsApiClient SharedItems { get; set; } = default!;

    [Inject]
    private NavigationManager Navigator { get; set; } = default!;

    private string CancelRoute => SharingRoutes.Secret(OwnerId, SecretId);

    protected override void OnInitialized() => Track(new ModelScrub(this));

    protected override async Task<ApiResponse<SharedSecretDto>> LoadAsync(CancellationToken cancellationToken)
    {
        _conflict = false;
        _formError = null;
        _accessEnded = false;
        var response = await SharedItems.GetSecretAsync(OwnerId, SecretId, cancellationToken);
        if (response.Status != ApiCallStatus.Success || response.Value is not { } shared)
        {
            return response;
        }

        var secret = shared.Item;
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

    private static bool CanEdit(SharedSecretDto shared) =>
        shared.Permission is SharePermissionDto.Edit or SharePermissionDto.Manage;

    private static string BlockedText(SharedSecretDto shared) =>
        shared.Item.IsDamaged ? SecretEditor.DamagedText : ViewOnlyText;

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

        if (Value is { Item.HasNotes: false })
        {
            _model.ReplaceNotes = true;
            return Task.CompletedTask;
        }

        _formError = null;
        var request = new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.View };
        return RunApiCommandAsync(
            token => SharedItems.RevealSecretAsync(OwnerId, SecretId, request, token),
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
            ReportFailure(response);
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
            _formError = SecretEditor.NameRequiredText;
            return Task.CompletedTask;
        }

        return RunApiCommandAsync(
            token => SharedItems.UpdateSecretAsync(OwnerId, SecretId, _concurrencyToken, SecretRequestMapper.ForUpdate(_model, _tagsText), token),
            Apply,
            ShowFailed);
    }

    private Task Apply(ApiResponse<SecretDto> response)
    {
        if (response.Status == ApiCallStatus.Success)
        {
            Navigator.NavigateTo(CancelRoute);
        }
        else if (response.Status == ApiCallStatus.Conflict)
        {
            _conflict = true;
        }
        else
        {
            ReportFailure(response);
        }

        return Task.CompletedTask;
    }

    private void ReportFailure<T>(ApiResponse<T> response)
    {
        if (response.Status == ApiCallStatus.NotFound)
        {
            EndAccess();
            return;
        }

        _formError = SharedLoadErrors.ForCommand(response);
    }

    private void EndAccess()
    {
        _accessEnded = true;
        _formError = null;
        _conflict = false;
        _model = new UpdateSecretRequestDto();
        _tagsText = string.Empty;
    }

    private sealed class ModelScrub(SharedSecretEditor editor) : IDisposable
    {
        public void Dispose()
        {
            editor._model.Value = null;
            editor._model.Notes = null;
        }
    }
}
