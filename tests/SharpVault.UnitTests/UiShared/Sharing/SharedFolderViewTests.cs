// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedFolderViewTests : SharedDetailTestHost
{
    private readonly Guid _folderId = Guid.NewGuid();
    private readonly Guid _parentId = Guid.NewGuid();
    private readonly Guid _childId = Guid.NewGuid();
    private readonly Guid _firstItemId = Guid.NewGuid();
    private readonly Guid _secondItemId = Guid.NewGuid();

    public SharedFolderViewTests()
    {
        GrantsClient.ListFolderGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(Guid.NewGuid(), "Grace Hopper")));
    }

    private SharedFolderContentsDto Contents(SharePermissionDto permission, string? token = null, params SecretDto[] items) => new()
    {
        FolderId = _folderId,
        Name = "Servers",
        OwnerId = OwnerId,
        OwnerDisplayName = "Ada Lovelace",
        Permission = permission,
        Path = [new SharedFolderSummaryDto { FolderId = _parentId, Name = "Infra" }, new SharedFolderSummaryDto { FolderId = _folderId, Name = "Servers" }],
        SubFolders = [new SharedFolderSummaryDto { FolderId = _childId, Name = "Racks" }],
        Items = [.. items],
        ContinuationToken = token,
    };

    private void Returns(SharedFolderContentsDto contents, string? from = null) =>
        SharedItemsClient.GetFolderAsync(OwnerId, _folderId, Arg.Any<int>(), from, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedFolderContentsDto>.Success(contents)));

    private static SecretDto Item(Guid id, string name) => new() { Id = id, Name = name, Type = SecretTypeDto.Login };

    private IRenderedComponent<CascadingValue<CurrentUserDto?>> RenderPage() =>
        RenderWithUser<SharedFolderPage>(Owner, parameters => parameters
            .Add(page => page.OwnerId, OwnerId)
            .Add(page => page.FolderId, _folderId));

    [Fact]
    public void Render_ShowsPathSubFoldersAndItemsWithSharedLinks()
    {
        Returns(Contents(SharePermissionDto.View, null, Item(_firstItemId, "Beta"), Item(_secondItemId, "alpha")));

        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-heading");
        Assert.Equal("Servers", cut.Find("#shared-folder-heading").TextContent.Trim());
        Assert.EndsWith(SharingRoutes.Folder(OwnerId, _parentId), cut.Find($"#shared-folder-crumb-{_parentId:D}").GetAttribute("href"), StringComparison.Ordinal);
        Assert.EndsWith(SharingRoutes.Folder(OwnerId, _childId), cut.Find($"#shared-subfolder-link-{_childId:D}").GetAttribute("href"), StringComparison.Ordinal);
        Assert.EndsWith(SharingRoutes.Secret(OwnerId, _firstItemId), cut.Find($"#shared-folder-secret-link-{_firstItemId:D}").GetAttribute("href"), StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace", cut.Find("#shared-folder-owner").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#share-panel"));
        GrantsClient.DidNotReceiveWithAnyArgs().ListFolderGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Manager_HasThePanelForTheSharedOwner()
    {
        Returns(Contents(SharePermissionDto.Manage));

        var cut = RenderPage();

        cut.WaitForElement("#share-add");
        GrantsClient.Received(1).ListFolderGrantsAsync(OwnerId, _folderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Editor_HasNoPanel()
    {
        Returns(Contents(SharePermissionDto.Edit));

        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-heading");
        Assert.Empty(cut.FindAll("#share-panel"));
    }

    [Fact]
    public void LoadMore_AppendsTheNextPage()
    {
        Returns(Contents(SharePermissionDto.View, "next", Item(_firstItemId, "Alpha")));
        Returns(Contents(SharePermissionDto.View, null, Item(_secondItemId, "Beta")), "next");
        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-load-more").Click();

        cut.WaitForElement($"#shared-folder-secret-link-{_secondItemId:D}");
        Assert.NotNull(cut.Find($"#shared-folder-secret-link-{_firstItemId:D}"));
        Assert.Empty(cut.FindAll("#shared-folder-load-more"));
    }

    [Fact]
    public void LoadMore_Fails_KeepsLoadedRowsAndShowsInlineError()
    {
        Returns(Contents(SharePermissionDto.View, "next", Item(_firstItemId, "Alpha")));
        SharedItemsClient.GetFolderAsync(OwnerId, _folderId, Arg.Any<int>(), "next", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedFolderContentsDto>.Failed()));
        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-load-more").Click();

        cut.WaitForElement("#shared-folder-load-more-error");
        Assert.NotNull(cut.Find($"#shared-folder-secret-link-{_firstItemId:D}"));
        Assert.NotNull(cut.Find("#shared-folder-load-more"));
    }

    [Fact]
    public void Empty_ShowsTheEmptyState()
    {
        var contents = Contents(SharePermissionDto.View);
        contents.SubFolders = [];
        Returns(contents);

        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-empty");
    }

    [Fact]
    public void Load_NotFound_ShowsAccessEnded()
    {
        SharedItemsClient.GetFolderAsync(OwnerId, _folderId, Arg.Any<int>(), null, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedFolderContentsDto>.NotFound()));

        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-error");
        Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-folder-error").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_RouteChangeToSubFolder_ReloadsForTheNewFolder()
    {
        Returns(Contents(SharePermissionDto.View));
        var cut = RenderPage();
        cut.WaitForElement("#shared-folder-heading");
        var contents = Contents(SharePermissionDto.View);
        contents.FolderId = _childId;
        contents.Name = "Racks";
        SharedItemsClient.GetFolderAsync(OwnerId, _childId, Arg.Any<int>(), null, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedFolderContentsDto>.Success(contents)));

        cut.Render(builder => builder
            .Add(cascade => cascade.Name, LayoutCascades.CurrentUser)
            .Add(cascade => cascade.Value, Owner)
            .AddChildContent<SharedFolderPage>(parameters => parameters
                .Add(page => page.OwnerId, OwnerId)
                .Add(page => page.FolderId, _childId)));

        cut.WaitForAssertion(() => Assert.Equal("Racks", cut.Find("#shared-folder-heading").TextContent.Trim()));
    }
}
