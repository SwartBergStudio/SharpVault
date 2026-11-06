// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

[Trait("Category", "Security")]
public sealed class DoubleSubmitGuardTests : VaultTestContext
{
    private const string Token = "etag-guard";
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(150);

    private static readonly Guid Folder = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private readonly SecretDto _secret = new()
    {
        Id = Guid.NewGuid(),
        Type = SecretTypeDto.Login,
        Name = "Guarded",
        HasValue = true,
        ConcurrencyToken = Token,
    };

    public DoubleSubmitGuardTests()
    {
        RenderDialogProviders();
        SecretsClient.GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretDto>.Success(_secret)));
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto
            {
                Items = [new FolderDto { Id = Folder, Name = "Finance" }],
            })));
    }

    private static TaskCompletionSource<ApiResponse<T>> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task InvokeTwiceAsync<TCut>(IRenderedComponent<TCut> cut, Func<Task> invoke, Action assertOneRequest, Action release)
        where TCut : IComponent
    {
        var first = cut.InvokeAsync(invoke);
        var second = cut.InvokeAsync(invoke);
        await Task.WhenAny(second, Task.Delay(SettleDelay));
        assertOneRequest();
        release();
        await Task.WhenAll(first, second);
    }

    private static T Instance<T>(IRenderedComponent<IComponent> cut, Func<T, bool>? where = null)
        where T : IComponent =>
        cut.FindComponents<T>().Select(found => found.Instance).First(where ?? (_ => true));

    [Fact]
    public async Task SecretEditor_SaveInvokedTwiceWhileCreating_SendsOneRequest()
    {
        var gate = Gate<SecretDto>();
        SecretsClient.CreateSecretAsync(Arg.Any<CreateSecretRequestDto>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<SecretEditor>();
        cut.WaitForElement("#secret-save");
        cut.Find("#secret-name-input").Input("Router");
        var save = Instance<SvButton>(cut, button => button.Id == "secret-save");

        await InvokeTwiceAsync(
            cut,
            () => save.OnClick.InvokeAsync(),
            () => SecretsClient.Received(1).CreateSecretAsync(Arg.Any<CreateSecretRequestDto>(), Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<SecretDto>.Success(_secret)));
    }

    [Fact]
    public async Task SecretEditor_SaveInvokedTwiceWhileUpdating_SendsOneRequest()
    {
        var gate = Gate<SecretDto>();
        SecretsClient.UpdateSecretAsync(_secret.Id, Token, Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<SecretEditor>(parameters => parameters.Add(editor => editor.SecretId, _secret.Id));
        cut.WaitForElement("#secret-save");
        var save = Instance<SvButton>(cut, button => button.Id == "secret-save");

        await InvokeTwiceAsync(
            cut,
            () => save.OnClick.InvokeAsync(),
            () => SecretsClient.Received(1).UpdateSecretAsync(_secret.Id, Token, Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<SecretDto>.Success(_secret)));
    }

    [Fact]
    public async Task SecretDetailView_DeleteConfirmedTwiceWhileDeleting_SendsOneRequest()
    {
        var gate = Gate<bool>();
        SecretsClient.DeleteSecretAsync(_secret.Id, Token, Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));
        cut.WaitForElement("#secret-heading");
        cut.Find("#secret-delete").Click();
        var dialog = Instance<ConfirmDialog>(cut, found => found.IdPrefix == "secret-delete-prompt");

        await InvokeTwiceAsync(
            cut,
            () => dialog.OnConfirm.InvokeAsync(),
            () => SecretsClient.Received(1).DeleteSecretAsync(_secret.Id, Token, Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<bool>.Success(true)));
    }

    [Fact]
    public async Task SecretDetailView_MoveConfirmedTwiceWhileMoving_SendsOneRequest()
    {
        var gate = Gate<SecretDto>();
        SecretsClient.MoveSecretAsync(_secret.Id, Token, Arg.Any<MoveSecretRequestDto>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));
        cut.WaitForElement("#secret-heading");
        cut.Find("#secret-move").Click();
        var dialog = Instance<MoveSecretDialog>(cut);

        await InvokeTwiceAsync(
            cut,
            () => dialog.OnConfirm.InvokeAsync(Folder),
            () => SecretsClient.Received(1).MoveSecretAsync(_secret.Id, Token, Arg.Any<MoveSecretRequestDto>(), Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<SecretDto>.Success(_secret)));
    }

    [Fact]
    public async Task FolderTreePanel_CreateSavedTwiceWhileCreating_SendsOneRequest()
    {
        var gate = Gate<FolderDto>();
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<FolderTreePanel>();
        cut.WaitForElement("#vault-new-folder");
        cut.Find("#vault-new-folder").Click();
        var dialog = Instance<FolderEditorDialog>(cut);

        await InvokeTwiceAsync(
            cut,
            () => dialog.OnSave.InvokeAsync(new FolderEditorInput("Payroll", null)),
            () => FoldersClient.Received(1).CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<FolderDto>.Success(new FolderDto { Id = Guid.NewGuid() })));
    }

    [Fact]
    public async Task FolderTreePanel_DeleteConfirmedTwiceWhileDeleting_SendsOneRequest()
    {
        var gate = Gate<bool>();
        FoldersClient.DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<FolderTreePanel>();
        OpenFolderAction(cut, Folder, "delete").Click();
        var dialog = Instance<FolderDeleteDialog>(cut);

        await InvokeTwiceAsync(
            cut,
            () => dialog.OnConfirm.InvokeAsync(FolderDeleteModeDto.DeleteContents),
            () => FoldersClient.Received(1).DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<bool>.Success(true)));
    }

    [Fact]
    public async Task RecycleBinView_RestoreInvokedTwiceWhileRestoring_SendsOneRequest()
    {
        var item = BinItem();
        ListBin(item);
        var gate = Gate<SecretDto>();
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<RecycleBinView>();
        cut.WaitForElement($"#{RecycleBinItemText.IdFor("bin-restore", item)}");
        var table = Instance<RecycleBinTable>(cut);

        await InvokeTwiceAsync(
            cut,
            () => table.OnRestore.InvokeAsync(item),
            () => RecycleBinClient.Received(1).RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<SecretDto>.Success(_secret)));
    }

    [Fact]
    public async Task RecycleBinView_PurgeConfirmedTwiceWhilePurging_SendsOneRequest()
    {
        var item = BinItem();
        ListBin(item);
        var gate = Gate<bool>();
        RecycleBinClient.PurgeSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<RecycleBinView>();
        cut.WaitForElement($"#{RecycleBinItemText.IdFor("bin-purge", item)}");
        cut.Find($"#{RecycleBinItemText.IdFor("bin-purge", item)}").Click();
        var dialog = Instance<RecyclePurgeDialog>(cut);

        await InvokeTwiceAsync(
            cut,
            () => dialog.OnConfirm.InvokeAsync(),
            () => RecycleBinClient.Received(1).PurgeSecretAsync(item.Id, Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<bool>.Success(true)));
    }

    [Fact]
    public async Task SecretHistoryView_RestoreConfirmedTwiceWhileRestoring_SendsOneRequest()
    {
        var version = new SecretVersionDto { VersionId = Guid.NewGuid(), ReplacedAt = DateTimeOffset.UnixEpoch, Revision = 1 };
        HistoryClient.ListVersionsAsync(_secret.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretVersionListDto>.Success(new SecretVersionListDto { Items = [version] })));
        var gate = Gate<SecretDto>();
        HistoryClient.RestoreVersionAsync(_secret.Id, version.VersionId, Token, Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<SecretHistoryView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));
        cut.WaitForElement($"#history-restore-{version.VersionId:D}");
        cut.Find($"#history-restore-{version.VersionId:D}").Click();
        var dialog = Instance<ConfirmDialog>(cut, found => found.IdPrefix == "history-restore-prompt");

        await InvokeTwiceAsync(
            cut,
            () => dialog.OnConfirm.InvokeAsync(),
            () => HistoryClient.Received(1).RestoreVersionAsync(_secret.Id, version.VersionId, Token, Arg.Any<CancellationToken>()),
            () => gate.SetResult(ApiResponse<SecretDto>.Success(_secret)));
    }

    private static RecycleBinItemDto BinItem() => new()
    {
        Kind = RecycleBinItemKindDto.Secret,
        Id = Guid.NewGuid(),
        Name = "Old",
        Type = SecretTypeDto.Login,
        DeletionBatchId = Guid.NewGuid(),
        DeletedAt = DateTimeOffset.UnixEpoch,
        PurgeAfter = DateTimeOffset.UnixEpoch.AddDays(30),
    };

    private void ListBin(RecycleBinItemDto item) =>
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<RecycleBinPageDto>.Success(new RecycleBinPageDto { Items = [item] })));
}
