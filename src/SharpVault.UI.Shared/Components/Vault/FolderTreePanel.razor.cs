// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UI.Shared.Components.Vault;

public partial class FolderTreePanel
{
    private readonly HashSet<Guid> _expanded = [];
    private IReadOnlyList<FolderNode> _nodes = [];
    private FolderEditorMode _editorMode = FolderEditorMode.Create;
    private bool _editorOpen;
    private FolderDto? _target;
    private Guid? _editorParentId;
    private FolderDto? _deleting;
    private string? _dialogError;
    private FolderDto? _sharing;

    [CascadingParameter(Name = LayoutCascades.CurrentUser)]
    private CurrentUserDto? CurrentUser { get; set; }

    private string SharingName => _sharing is null ? string.Empty : FolderNodes.NameOf(_sharing);

    private IReadOnlyList<FolderDto> AllFolders => Value?.Items ?? [];

    private string EditorInitialName => _editorMode == FolderEditorMode.Rename && _target is not null ? FolderNodes.NameOf(_target) : string.Empty;

    private string DeletingName => _deleting is null ? string.Empty : FolderNodes.NameOf(_deleting);

    protected override async Task<ApiResponse<FolderListDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var response = await FoldersClient.ListFoldersAsync(cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _nodes = FolderNodes.Build(response.Value.Items);
            ExpandAncestorsOfSelected(response.Value.Items);
        }

        return response;
    }

    protected override bool IsEmpty(FolderListDto value) => value.Items.Count == 0;

    protected override void OnParametersSet() => ExpandAncestorsOfSelected(AllFolders);

    private void ExpandAncestorsOfSelected(IReadOnlyList<FolderDto> folders)
    {
        if (SelectedFolderId is not { } selected)
        {
            return;
        }

        foreach (var ancestor in FolderNodes.AncestorIds(folders, selected))
        {
            _expanded.Add(ancestor);
        }
    }

    private Task ToggleAsync(Guid folderId)
    {
        if (!_expanded.Remove(folderId))
        {
            _expanded.Add(folderId);
        }

        return Task.CompletedTask;
    }

    private void OpenCreateRoot() => OpenEditor(FolderEditorMode.Create, null, null);

    private Task HandleActionAsync(FolderActionRequest request)
    {
        switch (request.Kind)
        {
            case FolderActionKind.NewChild:
                OpenEditor(FolderEditorMode.Create, null, request.Folder.Id);
                break;
            case FolderActionKind.Rename:
                OpenEditor(FolderEditorMode.Rename, request.Folder, null);
                break;
            case FolderActionKind.Move:
                OpenEditor(FolderEditorMode.Move, request.Folder, request.Folder.ParentId);
                break;
            case FolderActionKind.Delete:
                _dialogError = null;
                _deleting = request.Folder;
                break;
            case FolderActionKind.Share:
                _sharing = request.Folder;
                break;
        }

        return Task.CompletedTask;
    }

    private void OpenEditor(FolderEditorMode mode, FolderDto? target, Guid? parentId)
    {
        _dialogError = null;
        _editorMode = mode;
        _target = target;
        _editorParentId = parentId;
        _editorOpen = true;
    }

    private void CloseDialogs()
    {
        _editorOpen = false;
        _deleting = null;
        _sharing = null;
        _dialogError = null;
    }

    private Task SaveEditorAsync(FolderEditorInput input)
    {
        var parentToExpand = _editorMode == FolderEditorMode.Move ? input.ParentId : _editorParentId;
        return _editorMode switch
        {
            FolderEditorMode.Create => ExecuteAsync(token => FoldersClient.CreateFolderAsync(new CreateFolderRequestDto { Name = input.Name.Trim(), ParentId = _editorParentId }, token), parentToExpand, null),
            FolderEditorMode.Rename when _target is { } renamed => ExecuteAsync(token => FoldersClient.RenameFolderAsync(renamed.Id, new RenameFolderRequestDto { Name = input.Name.Trim() }, token), parentToExpand, null),
            FolderEditorMode.Move when _target is { } moved => ExecuteAsync(token => FoldersClient.MoveFolderAsync(moved.Id, new MoveFolderRequestDto { ParentId = input.ParentId }, token), parentToExpand, null),
            _ => Task.CompletedTask,
        };
    }

    private Task DeleteAsync(FolderDeleteModeDto mode)
    {
        if (_deleting is not { } folder)
        {
            return Task.CompletedTask;
        }

        var leavesSelection = SelectedFolderId is { } selected && WouldRemove(folder.Id, mode, selected);
        return ExecuteAsync(token => FoldersClient.DeleteFolderAsync(folder.Id, mode, token), null, leavesSelection ? VaultRoutes.Vault : null);
    }

    private bool WouldRemove(Guid deletedId, FolderDeleteModeDto mode, Guid selected) =>
        mode == FolderDeleteModeDto.DeleteContents
            ? FolderNodes.SubtreeIds(AllFolders, deletedId).Contains(selected)
            : deletedId == selected;

    private Task ExecuteAsync<T>(Func<CancellationToken, Task<ApiResponse<T>>> call, Guid? expandParent, string? navigateAfter)
    {
        _dialogError = null;
        return RunApiCommandAsync(
            call,
            response => ApplyResultAsync(response, expandParent, navigateAfter),
            () => _dialogError = VaultStatusText.FailedText);
    }

    private async Task ApplyResultAsync<T>(ApiResponse<T> response, Guid? expandParent, string? navigateAfter)
    {
        if (response.Status != ApiCallStatus.Success)
        {
            _dialogError = VaultStatusText.For(response);
            return;
        }

        if (expandParent is { } parent)
        {
            _expanded.Add(parent);
        }

        await CompleteAsync();
        if (navigateAfter is not null)
        {
            Nav.NavigateTo(navigateAfter);
        }
    }

    private async Task CompleteAsync()
    {
        CloseDialogs();
        await ReloadAsync();
        await OnChanged.InvokeAsync();
    }
}
