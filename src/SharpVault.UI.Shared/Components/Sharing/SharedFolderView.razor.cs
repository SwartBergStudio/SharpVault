// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UI.Shared.Components.Sharing;

public sealed partial class SharedFolderView
{
    public const int PageSize = 100;

    private List<SecretDto> _items = [];
    private string? _continuation;

    [Parameter, EditorRequired]
    public Guid OwnerId { get; set; }

    [Parameter, EditorRequired]
    public Guid FolderId { get; set; }

    [Inject]
    private ISharedItemsApiClient SharedItems { get; set; } = default!;

    protected override async Task<ApiResponse<SharedFolderContentsDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var response = await SharedItems.GetFolderAsync(OwnerId, FolderId, PageSize, null, cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is { } contents)
        {
            _items = [.. contents.Items.OrderBy(SecretDisplayName.For, StringComparer.OrdinalIgnoreCase)];
            _continuation = contents.ContinuationToken;
            LoadMoreFailed = false;
        }

        return response;
    }

    private static string FolderName(string? name, bool damaged) =>
        damaged || string.IsNullOrWhiteSpace(name) ? FolderNodes.DamagedName : name;

    private Task LoadMoreAsync() =>
        _continuation is null
            ? Task.CompletedTask
            : RunLoadMoreAsync(
                token => SharedItems.GetFolderAsync(OwnerId, FolderId, PageSize, _continuation, token),
                AppendPage,
                SharedLoadErrors.ForCommand,
                VaultStatusText.FailedText);

    private void AppendPage(SharedFolderContentsDto page)
    {
        _items = [.. _items.Concat(page.Items).OrderBy(SecretDisplayName.For, StringComparer.OrdinalIgnoreCase)];
        _continuation = page.ContinuationToken;
    }
}
