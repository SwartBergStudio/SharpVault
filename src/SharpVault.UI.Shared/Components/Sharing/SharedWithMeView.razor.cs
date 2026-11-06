// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UI.Shared.Components.Sharing;

public sealed partial class SharedWithMeView
{
    public const int PageSize = 100;
    public const string FoldersFailedText = "Your folders could not be loaded. Close this and try again.";

    private readonly StatusMessage _status = new();
    private List<SharedItemDto> _rows = [];
    private string? _continuation;
    private IReadOnlyList<FolderDto>? _folders;
    private bool _foldersLoading;
    private string? _fileError;
    private SharedItemDto? _filing;

    [Inject]
    private ISharedItemsApiClient SharedClient { get; set; } = default!;

    [Inject]
    private IVaultFoldersApiClient FoldersClient { get; set; } = default!;

    private string FilingName => _filing is null ? string.Empty : SharedItemText.Name(_filing);

    private Guid? FilingCurrentFolder => _filing is null ? null : SharedItemText.EffectiveFolderId(_filing, _folders);

    private IEnumerable<OwnerGroup> Groups => _rows
        .GroupBy(item => item.OwnerId)
        .Select(group => new OwnerGroup(
            group.Key,
            group.First().OwnerDisplayName,
            [.. group.OrderBy(SharedItemText.Name, StringComparer.OrdinalIgnoreCase)]))
        .OrderBy(group => group.OwnerName, StringComparer.OrdinalIgnoreCase);

    protected override async Task<ApiResponse<SharedItemPageDto>> LoadAsync(CancellationToken cancellationToken)
    {
        _rows = [];
        _continuation = null;
        var response = await FetchPageAsync(null, cancellationToken);
        if (response.Status != ApiCallStatus.Success)
        {
            return response;
        }

        var folders = await FoldersClient.ListFoldersAsync(cancellationToken);
        _folders = folders.Status == ApiCallStatus.Success && folders.Value is not null ? folders.Value.Items : null;
        return response;
    }

    protected override bool IsEmpty(SharedItemPageDto value) => _rows.Count == 0 && _continuation is null;

    private async Task<ApiResponse<SharedItemPageDto>> FetchPageAsync(string? continuationToken, CancellationToken cancellationToken)
    {
        var response = await SharedClient.ListAsync(null, PageSize, continuationToken, cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _rows = [.. _rows.Concat(response.Value.Items)];
            _continuation = response.Value.ContinuationToken;
        }

        return response;
    }

    private Task LoadMoreAsync() =>
        _continuation is null
            ? Task.CompletedTask
            : RunLoadMoreAsync(token => FetchPageAsync(_continuation, token), _ => { }, VaultStatusText.For, VaultStatusText.FailedText);

    private Task OpenFiling(SharedItemDto item)
    {
        _filing = item;
        _fileError = null;
        _foldersLoading = true;
        return RunApiCommandAsync(
            token => FoldersClient.ListFoldersAsync(token),
            ApplyFoldersAsync,
            ShowFoldersFailed);
    }

    private Task ApplyFoldersAsync(ApiResponse<FolderListDto> response)
    {
        _foldersLoading = false;
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _folders = response.Value.Items;
        }
        else
        {
            _fileError = FoldersFailedText;
        }

        return Task.CompletedTask;
    }

    private void ShowFoldersFailed()
    {
        _foldersLoading = false;
        _fileError = FoldersFailedText;
    }

    private void CloseFiling()
    {
        if (!IsCommandRunning)
        {
            _filing = null;
        }
    }

    private Task FileAsync(Guid? folderId)
    {
        if (_filing is not { } item || _folders is null)
        {
            return Task.CompletedTask;
        }

        _fileError = null;
        var request = new FileSharedItemRequestDto { FolderId = folderId };
        return RunApiCommandAsync(
            token => item.Kind == SharedItemKindDto.Folder
                ? SharedClient.FileFolderAsync(item.OwnerId, item.ItemId, request, token)
                : SharedClient.FileSecretAsync(item.OwnerId, item.ItemId, request, token),
            response => ApplyFilingAsync(item, folderId, response),
            () => _fileError = SharedItemStatusText.FilingFailed);
    }

    private async Task ApplyFilingAsync(SharedItemDto item, Guid? folderId, ApiResponse<bool> response)
    {
        switch (response.Status)
        {
            case ApiCallStatus.Success:
                item.FiledFolderId = folderId;
                _filing = null;
                _status.Show(folderId is null ? SharedItemStatusText.Unfiled : SharedItemStatusText.Filed, AlertKind.Success);
                break;
            case ApiCallStatus.NotFound:
                _filing = null;
                _status.Show(SharedItemStatusText.AccessEnded, AlertKind.Danger);
                await ReloadAsync();
                break;
            default:
                _fileError = SharedItemStatusText.FilingFailed;
                break;
        }
    }

    private sealed record OwnerGroup(Guid OwnerId, string OwnerName, IReadOnlyList<SharedItemDto> Items);
}
