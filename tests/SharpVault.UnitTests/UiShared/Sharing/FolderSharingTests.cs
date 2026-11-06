// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class FolderSharingTests : SharingTestHost
{
    private readonly Guid _folderId = Guid.NewGuid();
    private readonly Guid _colleagueId = Guid.NewGuid();

    public FolderSharingTests()
    {
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto
            {
                Items = [new FolderDto { Id = _folderId, Name = "Finance" }],
            })));
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto())));
        GrantsClient.ListFolderGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(_colleagueId, "Ada Lovelace")));
    }

    [Fact]
    public void FolderTreeNode_ShareButton_RaisesTheShareAction()
    {
        var folder = new FolderDto { Id = _folderId, Name = "Finance" };
        FolderActionRequest? raised = null;
        var node = FolderNodes.Build([folder])[0];

        var cut = Render<FolderTreeNode>(parameters => parameters
            .Add(component => component.Node, node)
            .Add(component => component.Expanded, new HashSet<Guid>())
            .Add(component => component.OnAction, request => raised = request));
        OpenFolderAction(cut, _folderId, "share").Click();

        Assert.Equal(FolderActionKind.Share, raised?.Kind);
        Assert.Equal(_folderId, raised?.Folder.Id);
    }

    [Fact]
    public void VaultPage_ShareFolder_OpensTheDialogWithTheOwnersFolderGrants()
    {
        var cut = RenderWithUser<VaultPage>(Owner, _ => { });
        OpenFolderAction(cut, _folderId, "share").Click();

        Provider.WaitForElement("#folder-sharing-title");
        Provider.WaitForElement($"#share-grant-name-{_colleagueId:D}");

        Assert.Contains("Finance", Provider.Find("#folder-sharing-title").TextContent, StringComparison.Ordinal);
        GrantsClient.Received(1).ListFolderGrantsAsync(Owner.UserId, _folderId, Arg.Any<CancellationToken>());
        GrantsClient.DidNotReceiveWithAnyArgs().ListSecretGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task VaultPage_ShareFolderThenClose_ClosesTheDialog()
    {
        var cut = RenderWithUser<VaultPage>(Owner, _ => { });
        OpenFolderAction(cut, _folderId, "share").Click();
        Provider.WaitForElement("#folder-sharing-close").Click();

        cut.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#folder-sharing-dialog")));
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    public void VaultPage_ShareFolderWithoutACurrentUser_OpensNothingAndListsNothing()
    {
        var cut = RenderWithUser<VaultPage>(null, _ => { });
        OpenFolderAction(cut, _folderId, "share").Click();

        Assert.Empty(Provider.FindAll("#folder-sharing-dialog"));
        GrantsClient.DidNotReceiveWithAnyArgs().ListFolderGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void VaultPage_FolderDeletePrompt_MentionsThatSharedPeopleLoseAccess()
    {
        var cut = RenderWithUser<VaultPage>(Owner, _ => { });
        OpenFolderAction(cut, _folderId, "delete").Click();

        var dialog = Provider.WaitForElement("#folder-delete-dialog");

        Assert.Contains(SharedItemStatusText.SharingNoticeOnDelete, dialog.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void VaultPage_FolderGrantRevoke_UsesTheFolderRoute()
    {
        GrantsClient.RevokeFolderGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = RenderWithUser<VaultPage>(Owner, _ => { });
        OpenFolderAction(cut, _folderId, "share").Click();
        Provider.WaitForElement($"#share-grant-revoke-{_colleagueId:D}").Click();

        Provider.WaitForElement("#share-revoke-prompt-confirm").Click();

        cut.WaitForAssertion(() => GrantsClient.Received(1).RevokeFolderGrantAsync(Owner.UserId, _folderId, _colleagueId, Arg.Any<CancellationToken>()));
    }
}
