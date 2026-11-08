// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedFolderViewPagingTests : SharedDetailTestHost
{
    private const string NextToken = "folder-page-two";

    private readonly Guid _folderId = Guid.NewGuid();
    private readonly Guid _parentId = Guid.NewGuid();
    private readonly Guid _childId = Guid.NewGuid();
    private readonly Guid _laterChildId = Guid.NewGuid();
    private readonly Guid _firstItemId = Guid.NewGuid();
    private readonly Guid _secondItemId = Guid.NewGuid();

    public SharedFolderViewPagingTests()
    {
        GrantsClient.ListFolderGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(Guid.NewGuid(), "Grace Hopper")));
    }

    private SharedFolderContentsDto Contents(SharePermissionDto permission, string? token, string childName, Guid childId, params SecretDto[] items) => new()
    {
        FolderId = _folderId,
        Name = "Servers",
        OwnerId = OwnerId,
        OwnerDisplayName = "Ada Lovelace",
        Permission = permission,
        Path = [new SharedFolderSummaryDto { FolderId = _parentId, Name = "Infra" }, new SharedFolderSummaryDto { FolderId = _folderId, Name = "Servers" }],
        SubFolders = [new SharedFolderSummaryDto { FolderId = childId, Name = childName }],
        Items = [.. items],
        ContinuationToken = token,
    };

    private static SecretDto Item(Guid id, string name) => new() { Id = id, Name = name, Type = SecretTypeDto.Login };

    private void ReturnsFirst(SharedFolderContentsDto contents) =>
        SharedItemsClient.GetFolderAsync(OwnerId, _folderId, Arg.Any<int>(), null, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedFolderContentsDto>.Success(contents)));

    private void ReturnsNext(ApiResponse<SharedFolderContentsDto> response) =>
        SharedItemsClient.GetFolderAsync(OwnerId, _folderId, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(response));

    private IRenderedComponent<CascadingValue<CurrentUserDto?>> RenderPage() =>
        RenderWithUser<SharedFolderPage>(Owner, parameters => parameters
            .Add(page => page.OwnerId, OwnerId)
            .Add(page => page.FolderId, _folderId));

    [Fact]
    public async Task LoadMore_KeepsThePathAndSubFoldersOfTheFirstResponseAndSortsTheItems()
    {
        ReturnsFirst(Contents(SharePermissionDto.View, NextToken, "Racks", _childId, Item(_firstItemId, "zulu")));
        ReturnsNext(ApiResponse<SharedFolderContentsDto>.Success(Contents(SharePermissionDto.View, null, "Later", _laterChildId, Item(_secondItemId, "Alpha"))));
        var cut = RenderPage();

        await cut.WaitForElement("#shared-folder-load-more").ClickAsync();

        cut.WaitForElement($"#shared-folder-secret-link-{_secondItemId:D}");
        Assert.NotNull(cut.Find($"#shared-subfolder-link-{_childId:D}"));
        Assert.Empty(cut.FindAll($"#shared-subfolder-link-{_laterChildId:D}"));
        Assert.Equal(2, cut.FindAll("#shared-folder-path a").Count);
        var order = cut.FindAll("[id^=shared-folder-secret-link-]").Select(link => link.TextContent.Trim()).ToList();
        Assert.Equal(["Alpha", "zulu"], order);
        Assert.Empty(cut.FindAll("#shared-folder-load-more"));
    }

    [Fact]
    public async Task LoadMore_SendsTheContinuationTokenOfTheFirstPageForTheSameOwnerAndFolder()
    {
        ReturnsFirst(Contents(SharePermissionDto.View, NextToken, "Racks", _childId, Item(_firstItemId, "Alpha")));
        ReturnsNext(ApiResponse<SharedFolderContentsDto>.Success(Contents(SharePermissionDto.View, null, "Racks", _childId)));
        var cut = RenderPage();

        await cut.WaitForElement("#shared-folder-load-more").ClickAsync();

        cut.WaitForAssertion(() => SharedItemsClient.Received(1).GetFolderAsync(OwnerId, _folderId, SharedFolderView.PageSize, NextToken, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task LoadMore_PermissionChangedInLaterPage_DoesNotUpgradeThePanelTheFirstResponseDecided()
    {
        ReturnsFirst(Contents(SharePermissionDto.View, NextToken, "Racks", _childId, Item(_firstItemId, "Alpha")));
        ReturnsNext(ApiResponse<SharedFolderContentsDto>.Success(Contents(SharePermissionDto.Manage, null, "Racks", _childId, Item(_secondItemId, "Beta"))));
        var cut = RenderPage();

        await cut.WaitForElement("#shared-folder-load-more").ClickAsync();

        cut.WaitForElement($"#shared-folder-secret-link-{_secondItemId:D}");
        Assert.Empty(cut.FindAll("#share-panel"));
        _ = GrantsClient.DidNotReceiveWithAnyArgs().ListFolderGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LoadMore_AccessEnded_ShowsTheAccessEndedTextInlineAndKeepsTheLoadedRows()
    {
        ReturnsFirst(Contents(SharePermissionDto.View, NextToken, "Racks", _childId, Item(_firstItemId, "Alpha")));
        ReturnsNext(ApiResponse<SharedFolderContentsDto>.NotFound());
        var cut = RenderPage();

        await cut.WaitForElement("#shared-folder-load-more").ClickAsync();

        cut.WaitForElement("#shared-folder-load-more-error");
        Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-folder-load-more-error").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#shared-folder-secret-link-{_firstItemId:D}"));
    }

    [Fact]
    public void Damaged_FolderAndSubFolder_ShowTheUnreadableNameNotTheStoredOne()
    {
        var contents = Contents(SharePermissionDto.View, null, "Hidden child", _childId, Item(_firstItemId, "Alpha"));
        contents.IsDamaged = true;
        contents.Name = "Hidden folder";
        contents.SubFolders[0].IsDamaged = true;
        ReturnsFirst(contents);

        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-heading");
        Assert.DoesNotContain("Hidden folder", cut.Find("#shared-folder-heading").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden child", cut.Find($"#shared-subfolder-link-{_childId:D}").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(SharePermissionDto.View, false)]
    [InlineData(SharePermissionDto.Manage, true)]
    [InlineData((SharePermissionDto)99, false)]
    public void Permission_DecidesWhetherTheSharePanelIsShown(SharePermissionDto permission, bool panel)
    {
        ReturnsFirst(Contents(permission, null, "Racks", _childId));

        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-heading");
        cut.WaitForAssertion(() => Assert.Equal(panel, cut.FindAll("#share-panel").Count == 1));
        if (!panel)
        {
            _ = GrantsClient.DidNotReceiveWithAnyArgs().ListFolderGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task SharedFolderView_LoadMoreSignInRequired_RedirectsOnce()
    {
        ReturnsFirst(Contents(SharePermissionDto.View, NextToken, "Racks", _childId, Item(_firstItemId, "Alpha")));
        ReturnsNext(ApiResponse<SharedFolderContentsDto>.SignInRequired());
        var cut = RenderPage();

        await cut.WaitForElement("#shared-folder-load-more").ClickAsync();

        cut.WaitForAssertion(() =>
        {
            var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
            Assert.Single(navigation.History, entry => entry.Uri.StartsWith(AccountRoutes.SignIn, StringComparison.Ordinal));
        });
        _ = SharedItemsClient.Received(1).GetFolderAsync(OwnerId, _folderId, SharedFolderView.PageSize, NextToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SharedFolderView_LoadMoreAccessDenied_GoesToAccessDenied()
    {
        ReturnsFirst(Contents(SharePermissionDto.View, NextToken, "Racks", _childId, Item(_firstItemId, "Alpha")));
        ReturnsNext(ApiResponse<SharedFolderContentsDto>.AccessDenied());
        var cut = RenderPage();

        await cut.WaitForElement("#shared-folder-load-more").ClickAsync();

        cut.WaitForAssertion(() =>
        {
            var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
            Assert.Contains(navigation.History, entry => entry.Uri == AccountRoutes.AccessDenied && entry.Options.ForceLoad);
        });
        Assert.Empty(cut.FindAll("#shared-folder-load-more-error"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SharedFolderView_LoadMoreThrows_KeepsRowsAndShowsTheFailedText()
    {
        ReturnsFirst(Contents(SharePermissionDto.View, NextToken, "Racks", _childId, Item(_firstItemId, "Alpha")));
        SharedItemsClient.GetFolderAsync(OwnerId, _folderId, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom-detail"));
        var cut = RenderPage();

        await cut.WaitForElement("#shared-folder-load-more").ClickAsync();

        cut.WaitForElement("#shared-folder-load-more-error");
        Assert.Contains(VaultStatusText.FailedText, cut.Find("#shared-folder-load-more-error").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("boom-detail", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#shared-folder-secret-link-{_firstItemId:D}"));
    }

    [Fact]
    public void SharedFolderView_LoadMoreDoubleClick_MakesOneCall()
    {
        ReturnsFirst(Contents(SharePermissionDto.View, NextToken, "Racks", _childId, Item(_firstItemId, "Alpha")));
        var gate = new TaskCompletionSource<ApiResponse<SharedFolderContentsDto>>();
        SharedItemsClient.GetFolderAsync(OwnerId, _folderId, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        var cut = RenderPage();

        cut.WaitForElement("#shared-folder-load-more").Click();
        cut.Find("#shared-folder-load-more").Click();
        gate.SetResult(ApiResponse<SharedFolderContentsDto>.Success(Contents(SharePermissionDto.View, null, "Racks", _childId, Item(_secondItemId, "Beta"))));

        cut.WaitForElement($"#shared-folder-secret-link-{_secondItemId:D}");
        SharedItemsClient.Received(1).GetFolderAsync(OwnerId, _folderId, SharedFolderView.PageSize, NextToken, Arg.Any<CancellationToken>());
    }
}
