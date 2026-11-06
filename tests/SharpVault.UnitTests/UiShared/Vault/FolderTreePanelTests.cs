// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class FolderTreePanelTests : VaultTestContext
{
    private const string Hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";

    private static readonly Guid Finance = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Payroll = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Bonuses = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid Legal = Guid.Parse("00000000-0000-0000-0000-0000000000d4");

    private readonly IRenderedComponent<MudDialogProvider> _provider;
    private int _changed;

    public FolderTreePanelTests()
    {
        _provider = RenderDialogProviders();
        ListReturns(Tree());
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderDto>.Success(new FolderDto { Id = Guid.NewGuid() })));
        FoldersClient.RenameFolderAsync(Arg.Any<Guid>(), Arg.Any<RenameFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderDto>.Success(new FolderDto())));
        FoldersClient.MoveFolderAsync(Arg.Any<Guid>(), Arg.Any<MoveFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderDto>.Success(new FolderDto())));
        FoldersClient.DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Success(true)));
    }

    private static FolderDto Folder(Guid id, Guid? parent, string? name, bool damaged = false) =>
        new() { Id = id, ParentId = parent, Name = name, IsDamaged = damaged };

    private static List<FolderDto> Tree() =>
    [
        Folder(Finance, null, "Finance"),
        Folder(Payroll, Finance, "Payroll"),
        Folder(Bonuses, Payroll, "Bonuses"),
        Folder(Legal, null, "Legal"),
    ];

    private void ListReturns(List<FolderDto> folders) =>
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto { Items = [.. folders] })));

    private IRenderedComponent<FolderTreePanel> RenderPanel(Guid? selected = null, bool unfiled = false)
    {
        var parameters = new Action<ComponentParameterCollectionBuilder<FolderTreePanel>>(builder =>
        {
            builder
                .Add(panel => panel.SelectedFolderId, selected)
                .Add(panel => panel.Unfiled, unfiled)
                .Add(panel => panel.OnChanged, () => _changed++);
        });
        return Render(parameters);
    }

    private int ListCalls() =>
        FoldersClient.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(FoldersClient.ListFoldersAsync));

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static string Node(Guid id) => $"#folder-node-{id:D}";

    private void FillAndSave(string name)
    {
        _provider.WaitForElement("#folder-editor-name-input").Input(name);
        _provider.WaitForAssertion(() => Assert.False(_provider.Find("#folder-editor-save").HasAttribute("disabled")));
        _provider.Find("#folder-editor-save").Click();
    }

    private void ConfirmDelete(string modeId)
    {
        _provider.WaitForElement($"#{modeId}").Click();
        _provider.WaitForAssertion(() => Assert.False(_provider.Find("#folder-delete-confirm").HasAttribute("disabled")));
        _provider.Find("#folder-delete-confirm").Click();
    }

    [Fact]
    public async Task FolderEditorDialog_DismissedWhileBusy_FailureIsStillVisible()
    {
        var pending = new TaskCompletionSource<ApiResponse<FolderDto>>();
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = RenderPanel();
        await cut.Find("#vault-new-folder").ClickAsync();
        FillAndSave("Payroll");
        cut.WaitForAssertion(() => FoldersClient.Received(1).CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>()));

        await PressEscapeAsync();
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        pending.SetResult(ApiResponse<FolderDto>.Invalid("Name is taken."));

        _provider.WaitForAssertion(() => Assert.Contains("Name is taken.", _provider.Find("#folder-editor-error").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FolderDeleteDialog_DismissedWhileBusy_FailureIsStillVisible()
    {
        ListReturns(Tree());
        var pending = new TaskCompletionSource<ApiResponse<bool>>();
        FoldersClient.DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = RenderPanel();
        cut.WaitForElement(Node(Legal));
        OpenAction(cut, Legal, "delete").Click();
        ConfirmDelete("folder-delete-mode-delete");
        cut.WaitForAssertion(() => FoldersClient.Received(1).DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>()));

        await PressEscapeAsync();
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        pending.SetResult(ApiResponse<bool>.Invalid("Folder cannot be deleted."));

        _provider.WaitForAssertion(() => Assert.Contains("Folder cannot be deleted.", _provider.Find("#folder-delete-error").TextContent, StringComparison.Ordinal));
    }

    private Task PressEscapeAsync() => _provider.Find(".sv-dialog__keys").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

    private IElement OpenAction(IRenderedComponent<FolderTreePanel> cut, Guid folderId, string action)
    {
        cut.Find($"#folder-actions-{folderId:D} button").Click();
        return PopoverProvider.WaitForElement($"#folder-{action}-{folderId:D}");
    }

    [Fact]
    public void Render_BuildsNestedTreeFromFlatList()
    {
        var cut = RenderPanel(selected: Bonuses);

        var finance = cut.Find(Node(Finance));
        Assert.NotNull(finance.QuerySelector($"#folder-node-{Payroll:D}"));
        Assert.NotNull(finance.QuerySelector($"#folder-node-{Bonuses:D}"));
        Assert.Equal(["Finance", "Payroll", "Bonuses", "Legal"], cut.FindAll("a[id^='folder-link-']").Select(link => link.TextContent.Trim()));
    }

    [Fact]
    public void Render_NotSelected_StartsWithEveryFolderCollapsed()
    {
        var cut = RenderPanel();

        Assert.NotNull(cut.Find(Node(Finance)));
        Assert.Empty(cut.FindAll(Node(Payroll)));
        Assert.Empty(cut.FindAll(Node(Bonuses)));
        Assert.NotNull(cut.Find($"#folder-toggle-{Finance:D}"));
        Assert.Empty(cut.FindAll($"#folder-toggle-{Legal:D}"));
    }

    [Fact]
    public void Render_AllAndUnfiledLinks_PointAtTheirRoutes()
    {
        var cut = RenderPanel();

        Assert.Equal(VaultRoutes.Vault, cut.Find("#folder-tree-all").GetAttribute("href"));
        Assert.Equal(VaultRoutes.Unfiled, cut.Find("#folder-tree-unfiled").GetAttribute("href"));
        Assert.NotNull(cut.Find("#vault-new-folder"));
    }

    [Fact]
    public void Toggle_ExpandsChildren()
    {
        var cut = RenderPanel();

        cut.Find($"#folder-toggle-{Finance:D}").Click();

        Assert.NotNull(cut.Find(Node(Payroll)));
        Assert.Empty(cut.FindAll(Node(Bonuses)));
        Assert.Contains("Collapse Finance", cut.Find($"#folder-toggle-{Finance:D}").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void Toggle_Twice_CollapsesAgain()
    {
        var cut = RenderPanel();

        cut.Find($"#folder-toggle-{Finance:D}").Click();
        cut.Find($"#folder-toggle-{Finance:D}").Click();

        Assert.Empty(cut.FindAll(Node(Payroll)));
        Assert.Contains("Expand Finance", cut.Find($"#folder-toggle-{Finance:D}").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedFolder_AncestorsAreExpanded()
    {
        var cut = RenderPanel(selected: Bonuses);

        Assert.NotNull(cut.Find(Node(Payroll)));
        Assert.NotNull(cut.Find(Node(Bonuses)));
        Assert.Contains("Collapse Finance", cut.Find($"#folder-toggle-{Finance:D}").GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Contains("Collapse Payroll", cut.Find($"#folder-toggle-{Payroll:D}").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedFolder_OtherBranchesStayCollapsed()
    {
        ListReturns([.. Tree(), Folder(Guid.Parse("00000000-0000-0000-0000-0000000000e5"), Legal, "Contracts")]);

        var cut = RenderPanel(selected: Bonuses);

        Assert.Empty(cut.FindAll("#folder-node-00000000-0000-0000-0000-0000000000e5"));
        Assert.Contains("Expand Legal", cut.Find($"#folder-toggle-{Legal:D}").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void FolderLink_PointsAtFolderRoute()
    {
        var cut = RenderPanel(selected: Bonuses);

        Assert.Equal(VaultRoutes.Folder(Payroll), cut.Find($"#folder-link-{Payroll:D}").GetAttribute("href"));
        Assert.Equal(VaultRoutes.Folder(Legal), cut.Find($"#folder-link-{Legal:D}").GetAttribute("href"));
    }

    [Fact]
    public void SelectedFolderLink_MarksAriaCurrentPage()
    {
        var cut = RenderPanel(selected: Payroll);

        Assert.Equal("page", cut.Find($"#folder-link-{Payroll:D}").GetAttribute("aria-current"));
        Assert.False(cut.Find($"#folder-link-{Finance:D}").HasAttribute("aria-current"));
        Assert.False(cut.Find($"#folder-link-{Legal:D}").HasAttribute("aria-current"));
        Assert.False(cut.Find("#folder-tree-all").HasAttribute("aria-current"));
    }

    [Fact]
    public void NothingSelected_AllItemsLinkIsCurrent()
    {
        var cut = RenderPanel();

        Assert.Equal("page", cut.Find("#folder-tree-all").GetAttribute("aria-current"));
        Assert.False(cut.Find("#folder-tree-unfiled").HasAttribute("aria-current"));
    }

    [Fact]
    public void UnfiledView_UnfiledLinkIsCurrentAndAllItemsIsNot()
    {
        var cut = RenderPanel(unfiled: true);

        Assert.Equal("page", cut.Find("#folder-tree-unfiled").GetAttribute("aria-current"));
        Assert.False(cut.Find("#folder-tree-all").HasAttribute("aria-current"));
    }

    [Fact]
    public void Render_Failed_ShowsErrorAndRetryReloadsTheTree()
    {
        var calls = 0;
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(++calls == 1
                ? ApiResponse<FolderListDto>.Failed()
                : ApiResponse<FolderListDto>.Success(new FolderListDto { Items = Tree() })));

        var cut = RenderPanel();

        Assert.NotNull(cut.Find("#folder-tree-error"));
        Assert.NotNull(cut.Find("#folder-tree-all"));
        cut.Find("#folder-tree-retry").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(Node(Finance))));
        Assert.Empty(cut.FindAll("#folder-tree-error"));
    }

    [Fact]
    public void Render_NoFolders_ShowsTheEmptyStateBesideTheFixedLinksAndNewFolder()
    {
        ListReturns([]);

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll("#folder-tree-error"));
        Assert.Empty(cut.FindAll("[id^='folder-node-']"));
        Assert.Contains("No folders yet", cut.Find("#folder-tree-empty").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#folder-tree-all"));
        Assert.NotNull(cut.Find("#folder-tree-unfiled"));
        Assert.NotNull(cut.Find("#vault-new-folder"));
    }

    [Fact]
    public void Render_NoFolders_ShowsAHint()
    {
        ListReturns([]);

        var cut = RenderPanel();

        Assert.NotNull(cut.Find("#folder-tree-empty-description"));
    }

    [Fact]
    public void Create_FirstFolderFromEmptyState_ShowsItInTheTree()
    {
        var folders = new List<FolderDto>();
        ListReturns(folders);
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                folders.Add(Folder(Finance, null, call.Arg<CreateFolderRequestDto>().Name));
                return Task.FromResult(ApiResponse<FolderDto>.Success(new FolderDto { Id = Finance }));
            });
        var cut = RenderPanel();
        cut.WaitForElement("#folder-tree-empty");

        cut.Find("#vault-new-folder").Click();
        FillAndSave("Finance");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(Node(Finance))));
        Assert.Empty(cut.FindAll("#folder-tree-empty"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Render_FolderNamesWithMarkup_AreEncodedEverywhere()
    {
        ListReturns([Folder(Finance, null, Hostile), Folder(Payroll, Finance, Hostile)]);

        var cut = RenderPanel(selected: Payroll);

        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("[onerror]"));
        Assert.Equal(Hostile, cut.Find($"#folder-link-{Finance:D}").TextContent.Trim());
        Assert.Contains(Hostile, cut.Find($"#folder-actions-{Finance:D} button").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DamagedFolder_ShowsUnreadableTextAndStaysUsable()
    {
        ListReturns([Folder(Finance, null, "Real name", damaged: true), Folder(Legal, null, null)]);

        var cut = RenderPanel();

        Assert.Equal("Unreadable folder", cut.Find($"#folder-link-{Finance:D}").TextContent.Trim());
        Assert.Equal("Unreadable folder", cut.Find($"#folder-link-{Legal:D}").TextContent.Trim());
        Assert.DoesNotContain("Real name", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#folder-actions-{Finance:D}"));
        Assert.Empty(cut.FindAll("#folder-tree-error"));
    }

    [Fact]
    public void Create_Success_ReloadsAndRaisesOnChanged()
    {
        var cut = RenderPanel();

        cut.Find("#vault-new-folder").Click();
        FillAndSave("  Operations  ");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).CreateFolderAsync(
            Arg.Is<CreateFolderRequestDto>(request => request.Name == "Operations" && request.ParentId == null),
            Arg.Any<CancellationToken>());
        Assert.Equal(2, ListCalls());
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-editor-dialog")));
    }

    [Fact]
    public void CreateChild_SendsTheParentAndExpandsIt()
    {
        var cut = RenderPanel();

        OpenAction(cut, Legal, "new-child").Click();
        FillAndSave("Contracts");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).CreateFolderAsync(
            Arg.Is<CreateFolderRequestDto>(request => request.Name == "Contracts" && request.ParentId == Legal),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Create_Rejected_ShowsDetailInDialog()
    {
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderDto>.Invalid("A folder with that name already exists.")));
        var cut = RenderPanel();

        cut.Find("#vault-new-folder").Click();
        FillAndSave("Finance");

        _provider.WaitForAssertion(() => Assert.Contains("A folder with that name already exists.", _provider.Find("#folder-editor-error").TextContent, StringComparison.Ordinal));
        Assert.NotNull(_provider.Find("#folder-editor-dialog"));
        Assert.Equal(0, _changed);
        Assert.Equal(1, ListCalls());
    }

    [Theory]
    [InlineData(ApiCallStatus.Conflict, VaultStatusText.ConflictText)]
    [InlineData(ApiCallStatus.Failed, VaultStatusText.FailedText)]
    [InlineData(ApiCallStatus.RateLimited, "Too many requests. Try again in a minute.")]
    [InlineData(ApiCallStatus.NotFound, "This item no longer exists.")]
    public void Create_Failure_ShowsTheStatusTextInDialog(ApiCallStatus status, string expected)
    {
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ApiResponse<FolderDto>(status, null)));
        var cut = RenderPanel();

        cut.Find("#vault-new-folder").Click();
        FillAndSave("Anything");

        _provider.WaitForAssertion(() => Assert.Contains(expected, _provider.Find("#folder-editor-error").TextContent, StringComparison.Ordinal));
        Assert.Equal(0, _changed);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Create_SignInRequired_NavigatesToSignInAndShowsNoDialogError()
    {
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderDto>.SignInRequired()));
        var cut = RenderPanel();

        cut.Find("#vault-new-folder").Click();
        FillAndSave("Anything");

        cut.WaitForAssertion(() => Assert.Contains("/account/sign-in", CurrentUri, StringComparison.Ordinal));
        Assert.Empty(_provider.FindAll("#folder-editor-error"));
        Assert.Equal(0, _changed);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Create_AccessDenied_InvalidatesAndNavigatesToAccessDenied()
    {
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderDto>.AccessDenied()));
        var cut = RenderPanel();

        cut.Find("#vault-new-folder").Click();
        FillAndSave("Anything");

        cut.WaitForAssertion(() => Assert.EndsWith("/account/access-denied", CurrentUri, StringComparison.Ordinal));
        Accessor.Received().Invalidate();
        Assert.Empty(_provider.FindAll("#folder-editor-error"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Create_Throws_ShowsFailedTextAndLogsTypeOnly()
    {
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns<Task<ApiResponse<FolderDto>>>(_ => throw new InvalidOperationException("secret-detail-do-not-log"));
        var cut = RenderPanel();

        cut.Find("#vault-new-folder").Click();
        FillAndSave("Anything");

        _provider.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, _provider.Find("#folder-editor-error").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("secret-detail-do-not-log", LogText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-detail-do-not-log", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_DoubleSave_SendsOneRequest()
    {
        var gate = new TaskCompletionSource<ApiResponse<FolderDto>>();
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();
        cut.Find("#vault-new-folder").Click();
        FillAndSave("Once");

        _provider.Find("#folder-editor-save").Click();
        gate.SetResult(ApiResponse<FolderDto>.Success(new FolderDto()));

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Cancel_ClosesTheDialogWithoutCallingTheApi()
    {
        var cut = RenderPanel();
        cut.Find("#vault-new-folder").Click();
        _provider.WaitForElement("#folder-editor-name-input").Input("Discarded");

        _provider.Find("#folder-editor-cancel").Click();

        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-editor-dialog")));
        FoldersClient.DidNotReceive().CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>());
        Assert.Equal(0, _changed);
    }

    [Fact]
    public async Task Reopen_AfterCancelledError_ShowsNoStaleErrorAndAnEmptyName()
    {
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderDto>.Invalid("Rejected once.")));
        var cut = RenderPanel();
        cut.Find("#vault-new-folder").Click();
        FillAndSave("Rejected");
        _provider.WaitForElement("#folder-editor-error");
        _provider.Find("#folder-editor-cancel").Click();
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-editor-dialog")));
        await WaitForClosedDialogsToSettleAsync();

        cut.Find("#vault-new-folder").Click();

        _provider.WaitForElement("#folder-editor-name-input");
        Assert.Empty(_provider.FindAll("#folder-editor-error"));
        Assert.Equal(string.Empty, _provider.Find("#folder-editor-name-input").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public void Rename_CallsRenameWithId()
    {
        var cut = RenderPanel();

        OpenAction(cut, Legal, "rename").Click();
        Assert.Equal("Legal", _provider.WaitForElement("#folder-editor-name-input").GetAttribute("value"));
        FillAndSave(" Compliance ");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).RenameFolderAsync(Legal, Arg.Is<RenameFolderRequestDto>(request => request.Name == "Compliance"), Arg.Any<CancellationToken>());
        Assert.Equal(2, ListCalls());
    }

    [Fact]
    public void DamagedFolder_HasNoRenameButOtherActions()
    {
        var damaged = Guid.Parse("00000000-0000-0000-0000-0000000000e5");
        ListReturns([Folder(damaged, null, null, damaged: true), Folder(Legal, null, "Legal")]);

        var cut = RenderPanel();

        cut.WaitForElement($"#folder-actions-{damaged:D}");
        OpenAction(cut, damaged, "move");
        Assert.Empty(PopoverProvider.FindAll($"#folder-rename-{damaged:D}"));
        Assert.NotEmpty(PopoverProvider.FindAll($"#folder-move-{damaged:D}"));
        OpenAction(cut, Legal, "rename");
        Assert.NotEmpty(PopoverProvider.FindAll($"#folder-rename-{Legal:D}"));
    }

    [Fact]
    public async Task Rename_OpenedForAnotherFolder_ReseedsTheName()
    {
        var cut = RenderPanel();
        OpenAction(cut, Legal, "rename").Click();
        _provider.WaitForElement("#folder-editor-name-input").Input("Half typed");
        _provider.Find("#folder-editor-cancel").Click();
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-editor-dialog")));
        await WaitForClosedDialogsToSettleAsync();

        OpenAction(cut, Finance, "rename").Click();

        Assert.Equal("Finance", _provider.WaitForElement("#folder-editor-name-input").GetAttribute("value"));
    }

    [Fact]
    public void Move_OffersNoSelfOrDescendant()
    {
        var cut = RenderPanel();

        OpenAction(cut, Finance, "move").Click();

        var values = _provider.WaitForElement("#folder-editor-parent-select").QuerySelectorAll("option").Select(option => option.GetAttribute("value")).ToList();
        Assert.Equal(["", Legal.ToString("D")], values);
        Assert.Empty(_provider.FindAll("#folder-editor-name-input"));
    }

    [Fact]
    public void Move_ChoosingAParent_CallsMoveWithThatParent()
    {
        var cut = RenderPanel(selected: Bonuses);
        OpenAction(cut, Bonuses, "move").Click();

        _provider.WaitForElement("#folder-editor-parent-select").Change(Legal.ToString("D"));
        _provider.Find("#folder-editor-save").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).MoveFolderAsync(Bonuses, Arg.Is<MoveFolderRequestDto>(request => request.ParentId == Legal), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Move_ToNoFolder_SendsANullParent()
    {
        var cut = RenderPanel(selected: Bonuses);
        OpenAction(cut, Bonuses, "move").Click();

        _provider.WaitForElement("#folder-editor-parent-select").Change(string.Empty);
        _provider.Find("#folder-editor-save").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).MoveFolderAsync(Bonuses, Arg.Is<MoveFolderRequestDto>(request => request.ParentId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Move_PreselectsTheCurrentParent()
    {
        var cut = RenderPanel(selected: Bonuses);
        OpenAction(cut, Bonuses, "move").Click();

        _provider.WaitForElement("#folder-editor-parent-select");
        _provider.Find("#folder-editor-save").Click();

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).MoveFolderAsync(Bonuses, Arg.Is<MoveFolderRequestDto>(request => request.ParentId == Payroll), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Delete_Dialog_StartsWithNoModeAndConfirmDisabledAndNamesTheFolder()
    {
        var cut = RenderPanel();

        OpenAction(cut, Legal, "delete").Click();

        _provider.WaitForElement("#folder-delete-dialog");
        Assert.True(_provider.Find("#folder-delete-confirm").HasAttribute("disabled"));
        Assert.Contains("Legal", _provider.Find("#folder-delete-dialog").TextContent, StringComparison.Ordinal);
        _provider.Find("#folder-delete-confirm").Click();
        FoldersClient.DidNotReceive().DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Delete_MoveMode_SendsMoveToParentAndReloads()
    {
        var cut = RenderPanel();
        OpenAction(cut, Legal, "delete").Click();

        ConfirmDelete("folder-delete-mode-move");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).DeleteFolderAsync(Legal, FolderDeleteModeDto.MoveToParent, Arg.Any<CancellationToken>());
        Assert.Equal(2, ListCalls());
        Assert.Equal("http://localhost/", CurrentUri);
    }

    [Fact]
    public void Delete_ContentsMode_SendsDeleteContents()
    {
        var cut = RenderPanel();
        OpenAction(cut, Legal, "delete").Click();

        ConfirmDelete("folder-delete-mode-delete");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        FoldersClient.Received(1).DeleteFolderAsync(Legal, FolderDeleteModeDto.DeleteContents, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Delete_SelectedFolder_NavigatesToVault()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(VaultRoutes.Folder(Legal));
        var cut = RenderPanel(selected: Legal);
        OpenAction(cut, Legal, "delete").Click();

        ConfirmDelete("folder-delete-mode-move");

        cut.WaitForAssertion(() => Assert.Equal($"http://localhost{VaultRoutes.Vault}", CurrentUri));
    }

    [Fact]
    public void Delete_AncestorOfSelectedWithDeleteContents_NavigatesToVault()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(VaultRoutes.Folder(Bonuses));
        var cut = RenderPanel(selected: Bonuses);
        OpenAction(cut, Finance, "delete").Click();

        ConfirmDelete("folder-delete-mode-delete");

        cut.WaitForAssertion(() => Assert.Equal($"http://localhost{VaultRoutes.Vault}", CurrentUri));
    }

    [Fact]
    public void Delete_AncestorOfSelectedWithMoveMode_StaysOnTheSelectedFolder()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(VaultRoutes.Folder(Bonuses));
        var cut = RenderPanel(selected: Bonuses);
        OpenAction(cut, Finance, "delete").Click();

        ConfirmDelete("folder-delete-mode-move");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        Assert.Equal($"http://localhost{VaultRoutes.Folder(Bonuses)}", CurrentUri);
    }

    [Fact]
    public void Delete_UnrelatedFolder_DoesNotNavigate()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(VaultRoutes.Folder(Finance));
        var cut = RenderPanel(selected: Finance);
        OpenAction(cut, Legal, "delete").Click();

        ConfirmDelete("folder-delete-mode-delete");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        Assert.Equal($"http://localhost{VaultRoutes.Folder(Finance)}", CurrentUri);
    }

    [Fact]
    public void Delete_Failure_ShowsErrorKeepsTheDialogAndDoesNotNavigate()
    {
        FoldersClient.DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Conflict()));
        Services.GetRequiredService<NavigationManager>().NavigateTo(VaultRoutes.Folder(Legal));
        var cut = RenderPanel(selected: Legal);
        OpenAction(cut, Legal, "delete").Click();

        ConfirmDelete("folder-delete-mode-delete");

        _provider.WaitForAssertion(() => Assert.Contains(VaultStatusText.ConflictText, _provider.Find("#folder-delete-error").TextContent, StringComparison.Ordinal));
        Assert.Equal($"http://localhost{VaultRoutes.Folder(Legal)}", CurrentUri);
        Assert.Equal(0, _changed);
        Assert.Equal(1, ListCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Delete_SignInRequired_NavigatesToSignInAndKeepsTheTree()
    {
        FoldersClient.DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.SignInRequired()));
        var cut = RenderPanel();
        OpenAction(cut, Legal, "delete").Click();

        ConfirmDelete("folder-delete-mode-delete");

        cut.WaitForAssertion(() => Assert.Contains("/account/sign-in", CurrentUri, StringComparison.Ordinal));
        Assert.Empty(_provider.FindAll("#folder-delete-error"));
        Assert.Equal(0, _changed);
    }

    [Fact]
    public void Delete_Cancel_ClosesWithoutCallingTheApi()
    {
        var cut = RenderPanel();
        OpenAction(cut, Legal, "delete").Click();
        _provider.WaitForElement("#folder-delete-cancel").Click();

        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-delete-dialog")));
        FoldersClient.DidNotReceive().DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_ReopenedAfterChoosingDelete_RequiresAFreshChoice()
    {
        var cut = RenderPanel();
        OpenAction(cut, Legal, "delete").Click();
        _provider.WaitForElement("#folder-delete-mode-delete").Click();
        _provider.WaitForAssertion(() => Assert.False(_provider.Find("#folder-delete-confirm").HasAttribute("disabled")));
        _provider.Find("#folder-delete-cancel").Click();
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-delete-dialog")));
        await WaitForClosedDialogsToSettleAsync();

        OpenAction(cut, Finance, "delete").Click();

        _provider.WaitForElement("#folder-delete-confirm");
        Assert.True(_provider.Find("#folder-delete-confirm").HasAttribute("disabled"));
    }

    [Fact]
    public void Reload_KeepsTheFoldersTheUserExpanded()
    {
        var cut = RenderPanel();
        cut.Find($"#folder-toggle-{Finance:D}").Click();
        cut.Find("#vault-new-folder").Click();

        FillAndSave("Operations");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        Assert.NotNull(cut.Find(Node(Payroll)));
        Assert.Contains("Collapse Finance", cut.Find($"#folder-toggle-{Finance:D}").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void Reload_RestoresTheAncestorsOfTheSelectedFolder()
    {
        var cut = RenderPanel(selected: Bonuses);

        cut.Find("#vault-new-folder").Click();
        FillAndSave("Operations");

        cut.WaitForAssertion(() => Assert.Equal(1, _changed));
        Assert.NotNull(cut.Find(Node(Bonuses)));
    }

    [Fact]
    public void Reload_AfterTheListChanges_ShowsTheNewShape()
    {
        var cut = RenderPanel();
        var created = Folder(Guid.Parse("00000000-0000-0000-0000-0000000000f6"), null, "Operations");
        ListReturns([.. Tree(), created]);
        cut.Find("#vault-new-folder").Click();

        FillAndSave("Operations");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#folder-node-{created.Id:D}")));
    }

    [Fact]
    public void CreateChild_ExpandsTheParentAfterReload()
    {
        var childOfLegal = Folder(Guid.Parse("00000000-0000-0000-0000-0000000000f6"), Legal, "Contracts");
        var cut = RenderPanel();
        ListReturns([.. Tree(), childOfLegal]);
        OpenAction(cut, Legal, "new-child").Click();

        FillAndSave("Contracts");

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#folder-node-{childOfLegal.Id:D}")));
    }

    [Fact]
    public void Dispose_WhileLoading_IgnoresTheLateResponse()
    {
        var gate = new TaskCompletionSource<ApiResponse<FolderListDto>>();
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();

        cut.Dispose();
        gate.SetResult(ApiResponse<FolderListDto>.Success(new FolderListDto { Items = Tree() }));

        Assert.DoesNotContain("Loading failed", LogText, StringComparison.Ordinal);
    }
}
