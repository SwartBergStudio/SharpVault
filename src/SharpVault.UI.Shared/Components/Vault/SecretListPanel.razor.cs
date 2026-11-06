// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Clipboard;

namespace SharpVault.UI.Shared.Components.Vault;

public partial class SecretListPanel
{
    public const int BrowsePageSize = 200;
    public const int SearchPageSize = 50;
    public const int BrowseAutoLoadLimit = 1000;
    public const int MaxEmptyPagesPerLoad = 10;
    public const string CopyFailedText = "Copy failed. Open the item and reveal it instead.";

    private List<SecretDto> _rows = [];
    private string? _continuation;
    private readonly StatusMessage _status = new();

    [Inject]
    private IVaultSecretsApiClient SecretsClient { get; set; } = default!;

    [Inject]
    private VaultUiSettings Settings { get; set; } = default!;

    [Inject]
    private SecretClipboard Clipboard { get; set; } = default!;

    [Parameter]
    public Guid? FolderId { get; set; }

    [Parameter]
    public bool Unfiled { get; set; }

    [Parameter]
    public string? Search { get; set; }

    private bool IsSearch => !string.IsNullOrWhiteSpace(Search);

    private int PageSize => IsSearch ? SearchPageSize : BrowsePageSize;

    private string EmptyTitle => IsSearch ? "No items match your search" : "No items here yet";

    private string EmptyDescription => IsSearch
        ? "Try a different search term, or select Clear to see all items."
        : "Select New item to add a password or secret.";

    protected override async Task<ApiResponse<SecretPageDto>> LoadAsync(CancellationToken cancellationToken)
    {
        _rows = [];
        _continuation = null;
        var response = await FetchPageAsync(null, cancellationToken);
        var emptyPages = EmptyPageCount(response);
        while (response.Status == ApiCallStatus.Success && ShouldKeepLoading(emptyPages))
        {
            response = await FetchPageAsync(_continuation, cancellationToken);
            emptyPages += EmptyPageCount(response);
        }

        return response;
    }

    protected override bool IsEmpty(SecretPageDto value) => _rows.Count == 0 && _continuation is null;

    private static int EmptyPageCount(ApiResponse<SecretPageDto> response) =>
        response.Value?.Items.Count == 0 ? 1 : 0;

    private bool ShouldKeepLoading(int emptyPages)
    {
        if (_continuation is null)
        {
            return false;
        }

        return IsSearch
            ? _rows.Count == 0 && emptyPages < MaxEmptyPagesPerLoad
            : _rows.Count < BrowseAutoLoadLimit;
    }

    private async Task<ApiResponse<SecretPageDto>> FetchPageAsync(string? continuationToken, CancellationToken cancellationToken)
    {
        var term = IsSearch ? Search!.Trim() : null;
        var response = await SecretsClient.ListSecretsAsync(FolderId, Unfiled, term, PageSize, continuationToken, cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _rows = [.. _rows.Concat(response.Value.Items).OrderBy(SecretDisplayName.For, StringComparer.OrdinalIgnoreCase)];
            _continuation = response.Value.ContinuationToken;
        }

        return response;
    }

    private Task LoadMoreAsync() =>
        _continuation is null
            ? Task.CompletedTask
            : RunLoadMoreAsync(token => FetchPageAsync(_continuation, token), _ => { }, VaultStatusText.For, VaultStatusText.FailedText);

    private Task CopyAsync(SecretDto secret)
    {
        var request = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.Copy };
        return RunApiCommandAsync(
            cancellationToken => SecretsClient.RevealSecretAsync(secret.Id, request, cancellationToken),
            ApplyCopyAsync,
            ShowCopyFailed);
    }

    private async Task ApplyCopyAsync(ApiResponse<RevealedValueDto> response)
    {
        var copied = response.Status == ApiCallStatus.Success
            && response.Value is not null
            && await Clipboard.CopyAsync(response.Value.Value, Cts.Token);
        _status.Show(copied ? SecretStatusText.Copied(Settings) : CopyFailedText, copied ? AlertKind.Success : AlertKind.Danger);
    }

    private void ShowCopyFailed() => _status.Show(CopyFailedText, AlertKind.Danger);
}
