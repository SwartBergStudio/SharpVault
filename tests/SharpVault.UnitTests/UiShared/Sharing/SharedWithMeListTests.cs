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
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedWithMeListTests : SharingTestHost
{
    private const string NextToken = "page-two";

    private readonly Guid _adaId = Guid.NewGuid();
    private readonly Guid _firstSecretId = Guid.NewGuid();
    private readonly Guid _secondSecretId = Guid.NewGuid();
    private readonly Guid _financeId = Guid.NewGuid();

    public SharedWithMeListTests()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(SharingRoutes.Shared);
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto
            {
                Items = [new FolderDto { Id = _financeId, Name = "Finance" }],
            })));
    }

    private static SharedItemDto Secret(Guid ownerId, string ownerName, Guid itemId, string name, bool damaged = false, Guid? filed = null) => new()
    {
        OwnerId = ownerId,
        OwnerDisplayName = ownerName,
        ItemId = itemId,
        Kind = SharedItemKindDto.Secret,
        Name = damaged ? null : name,
        Type = SecretTypeDto.Login,
        Permission = SharePermissionDto.View,
        IsDamaged = damaged,
        FiledFolderId = filed,
    };

    private static Task<ApiResponse<SharedItemPageDto>> PageOf(string? token, params SharedItemDto[] items) =>
        Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto { Items = [.. items], ContinuationToken = token }));

    private void ReturnsFirstPage(string? token, params SharedItemDto[] items) =>
        SharedClient.ListAsync(null, Arg.Any<int>(), null, Arg.Any<CancellationToken>()).Returns(_ => PageOf(token, items));

    private void ReturnsNextPage(Task<ApiResponse<SharedItemPageDto>> response) =>
        SharedClient.ListAsync(null, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>()).Returns(_ => response);

    private void AssertRedirectedToSignIn()
    {
        var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
        Assert.StartsWith(AccountRoutes.SignIn, navigation.History.First().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadMore_AppendsTheNextPageIntoTheOwnerGroupAndRemovesTheButton()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        ReturnsNextPage(PageOf(null, Secret(_adaId, "Ada Lovelace", _secondSecretId, "Alpha login")));
        var cut = Render<SharedWithMePage>();

        await cut.WaitForElement("#shared-load-more").ClickAsync();

        cut.WaitForElement($"#shared-item-link-secret-{_secondSecretId:D}");
        Assert.NotNull(cut.Find($"#shared-item-link-secret-{_firstSecretId:D}"));
        Assert.Single(cut.FindAll("[id^=shared-owner-heading-]"));
        Assert.Empty(cut.FindAll("#shared-load-more"));
        var names = cut.FindAll($"#shared-owner-{_adaId:D} [id^=shared-item-link-]").Select(link => link.TextContent.Trim()).ToList();
        Assert.Equal(["Alpha login", "Zulu login"], names);
        _ = SharedClient.Received(1).ListAsync(null, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadMore_Fails_KeepsTheRowsShowsTheInlineErrorAndKeepsTheButton()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        ReturnsNextPage(Task.FromResult(ApiResponse<SharedItemPageDto>.Failed()));
        var cut = Render<SharedWithMePage>();

        await cut.WaitForElement("#shared-load-more").ClickAsync();

        cut.WaitForElement("#shared-load-more-error");
        Assert.NotNull(cut.Find($"#shared-item-link-secret-{_firstSecretId:D}"));
        Assert.NotNull(cut.Find("#shared-load-more"));
        Assert.Empty(cut.FindAll("#shared-items-error"));
    }

    [Fact]
    public async Task LoadMore_RetryAfterFailure_AppendsAndClearsTheError()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        ReturnsNextPage(Task.FromResult(ApiResponse<SharedItemPageDto>.Failed()));
        var cut = Render<SharedWithMePage>();
        await cut.WaitForElement("#shared-load-more").ClickAsync();
        cut.WaitForElement("#shared-load-more-error");
        ReturnsNextPage(PageOf(null, Secret(_adaId, "Ada Lovelace", _secondSecretId, "Alpha login")));

        await cut.Find("#shared-load-more").ClickAsync();

        cut.WaitForElement($"#shared-item-link-secret-{_secondSecretId:D}");
        Assert.Empty(cut.FindAll("#shared-load-more-error"));
    }

    [Fact]
    public async Task LoadMore_SignInRequired_RedirectsToSignIn()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        ReturnsNextPage(Task.FromResult(ApiResponse<SharedItemPageDto>.SignInRequired()));
        var cut = Render<SharedWithMePage>();

        await cut.WaitForElement("#shared-load-more").ClickAsync();

        cut.WaitForAssertion(AssertRedirectedToSignIn);
        Assert.Empty(cut.FindAll("#shared-load-more-error"));
    }

    [Fact]
    public async Task EmptyFirstPageWithAToken_ShowsLoadMoreNotTheEmptyHint()
    {
        ReturnsFirstPage(NextToken);
        ReturnsNextPage(PageOf(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Found later")));
        var cut = Render<SharedWithMePage>();

        var button = cut.WaitForElement("#shared-load-more");

        Assert.Empty(cut.FindAll("#shared-items-empty"));
        Assert.Empty(cut.FindAll("[id^=shared-owner-heading-]"));
        await button.ClickAsync();
        cut.WaitForElement($"#shared-item-link-secret-{_firstSecretId:D}");
        Assert.Empty(cut.FindAll("#shared-items-empty"));
    }

    [Fact]
    public void Damaged_ShowsTheMarkerAndTheUnreadableNameWithoutTheStoredName()
    {
        ReturnsFirstPage(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Hidden name", damaged: true));

        var cut = Render<SharedWithMePage>();

        cut.WaitForElement($"#shared-item-damaged-secret-{_firstSecretId:D}");
        Assert.Equal(SharedItemText.UnreadableName, cut.Find($"#shared-item-link-secret-{_firstSecretId:D}").TextContent.Trim());
        Assert.DoesNotContain("Hidden name", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Undamaged_ShowsNoDamagedMarker()
    {
        ReturnsFirstPage(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Healthy"));

        var cut = Render<SharedWithMePage>();

        cut.WaitForElement($"#shared-item-link-secret-{_firstSecretId:D}");
        Assert.Empty(cut.FindAll($"#shared-item-damaged-secret-{_firstSecretId:D}"));
    }

    [Fact]
    public async Task FileSecret_SignInRequired_RedirectsToSignInAndShowsNoSuccessStatus()
    {
        ReturnsFirstPage(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Payroll"));
        SharedClient.FileSecretAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<FileSharedItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.SignInRequired()));
        var cut = Render<SharedWithMePage>();
        await cut.WaitForElement($"#shared-item-file-secret-{_firstSecretId:D}").ClickAsync();
        var confirm = Provider.WaitForElement("#file-shared-confirm");

        await confirm.ClickAsync();

        cut.WaitForAssertion(AssertRedirectedToSignIn);
        Assert.Empty(cut.FindAll("#shared-status"));
    }

    [Fact]
    public async Task OpenFiling_SignInRequired_RedirectsToSignIn()
    {
        ReturnsFirstPage(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Payroll"));
        var cut = Render<SharedWithMePage>();
        cut.WaitForElement($"#shared-item-file-secret-{_firstSecretId:D}");
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.SignInRequired()));

        await cut.Find($"#shared-item-file-secret-{_firstSecretId:D}").ClickAsync();

        cut.WaitForAssertion(AssertRedirectedToSignIn);
    }

    [Fact]
    public async Task FileSecret_FiledInAKnownFolder_PreselectsItAndSendsItOnConfirm()
    {
        ReturnsFirstPage(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Payroll", filed: _financeId));
        SharedClient.FileSecretAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<FileSharedItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = Render<SharedWithMePage>();
        await cut.WaitForElement($"#shared-item-file-secret-{_firstSecretId:D}").ClickAsync();
        var confirm = Provider.WaitForElement("#file-shared-confirm");

        await confirm.ClickAsync();

        cut.WaitForAssertion(() => SharedClient.Received(1).FileSecretAsync(_adaId, _firstSecretId, Arg.Is<FileSharedItemRequestDto>(request => request.FolderId == _financeId), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task FileSecret_FiledFolderGone_PreselectsNoFolder()
    {
        ReturnsFirstPage(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Payroll", filed: Guid.NewGuid()));
        SharedClient.FileSecretAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<FileSharedItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = Render<SharedWithMePage>();
        await cut.WaitForElement($"#shared-item-file-secret-{_firstSecretId:D}").ClickAsync();
        var confirm = Provider.WaitForElement("#file-shared-confirm");

        await confirm.ClickAsync();

        cut.WaitForAssertion(() => SharedClient.Received(1).FileSecretAsync(_adaId, _firstSecretId, Arg.Is<FileSharedItemRequestDto>(request => request.FolderId == null), Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task FilingDialog_Cancel_ClosesWithoutCallingTheApi()
    {
        ReturnsFirstPage(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Payroll"));
        var cut = Render<SharedWithMePage>();
        await cut.WaitForElement($"#shared-item-file-secret-{_firstSecretId:D}").ClickAsync();
        var cancel = Provider.WaitForElement("#file-shared-cancel");

        await cancel.ClickAsync();

        Provider.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#file-shared-confirm")));
        _ = SharedClient.DidNotReceiveWithAnyArgs().FileSecretAsync(default, default, default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FilingDialog_FolderListNotLoaded_DisablesConfirm()
    {
        ReturnsFirstPage(null, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Payroll"));
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Failed()));
        var cut = Render<SharedWithMePage>();
        cut.WaitForElement($"#shared-item-file-secret-{_firstSecretId:D}");

        await cut.Find($"#shared-item-file-secret-{_firstSecretId:D}").ClickAsync();

        Provider.WaitForElement("#file-shared-error");
        Assert.True(Provider.Find("#file-shared-confirm").HasAttribute("disabled"));
    }

    [Fact]
    public void SideNav_SharedLink_PointsAtTheSharedRoute()
    {
        Authorization.SetAuthorized("Owner One");
        var cut = Render<SideNav>(parameters => parameters
            .Add(nav => nav.Open, true)
            .AddCascadingValue(LayoutCascades.CurrentUser, new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = "Owner One" }));

        Assert.EndsWith(SharingRoutes.Shared, cut.Find("#nav-shared").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedWithMeView_LoadMoreSignInRequired_RedirectsOnce()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        ReturnsNextPage(Task.FromResult(ApiResponse<SharedItemPageDto>.SignInRequired()));
        var cut = Render<SharedWithMePage>();

        await cut.WaitForElement("#shared-load-more").ClickAsync();

        cut.WaitForAssertion(() =>
        {
            var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
            Assert.Single(navigation.History, entry => entry.Uri.StartsWith(AccountRoutes.SignIn, StringComparison.Ordinal));
        });
        _ = SharedClient.Received(1).ListAsync(null, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SharedWithMeView_LoadMoreAccessDenied_GoesToAccessDenied()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        ReturnsNextPage(Task.FromResult(ApiResponse<SharedItemPageDto>.AccessDenied()));
        var cut = Render<SharedWithMePage>();

        await cut.WaitForElement("#shared-load-more").ClickAsync();

        cut.WaitForAssertion(() =>
        {
            var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
            Assert.Contains(navigation.History, entry => entry.Uri == AccountRoutes.AccessDenied && entry.Options.ForceLoad);
        });
        Assert.Empty(cut.FindAll("#shared-load-more-error"));
    }

    [Fact]
    public async Task SharedWithMeView_LoadMoreRateLimited_KeepsRowsAndShowsTheServerText()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        ReturnsNextPage(Task.FromResult(ApiResponse<SharedItemPageDto>.RateLimited()));
        var cut = Render<SharedWithMePage>();

        await cut.WaitForElement("#shared-load-more").ClickAsync();

        cut.WaitForElement("#shared-load-more-error");
        Assert.Contains(LoadableComponentBase<SharedItemPageDto>.RateLimitedText, cut.Find("#shared-load-more-error").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#shared-item-link-secret-{_firstSecretId:D}"));
        Assert.NotNull(cut.Find("#shared-load-more"));
    }

    [Fact]
    public async Task SharedWithMeView_LoadMoreThrows_KeepsRowsAndShowsTheFailedText()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        SharedClient.ListAsync(null, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("boom-detail"));
        var cut = Render<SharedWithMePage>();

        await cut.WaitForElement("#shared-load-more").ClickAsync();

        cut.WaitForElement("#shared-load-more-error");
        Assert.Contains(VaultStatusText.FailedText, cut.Find("#shared-load-more-error").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("boom-detail", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#shared-item-link-secret-{_firstSecretId:D}"));
    }

    [Fact]
    public void SharedWithMeView_LoadMoreDoubleClick_MakesOneCall()
    {
        ReturnsFirstPage(NextToken, Secret(_adaId, "Ada Lovelace", _firstSecretId, "Zulu login"));
        var gate = new TaskCompletionSource<ApiResponse<SharedItemPageDto>>();
        SharedClient.ListAsync(null, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        var cut = Render<SharedWithMePage>();

        cut.WaitForElement("#shared-load-more").Click();
        cut.Find("#shared-load-more").Click();
        gate.SetResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto { Items = [Secret(_adaId, "Ada Lovelace", _secondSecretId, "Alpha login")] }));

        cut.WaitForElement($"#shared-item-link-secret-{_secondSecretId:D}");
        _ = SharedClient.Received(1).ListAsync(null, Arg.Any<int>(), NextToken, Arg.Any<CancellationToken>());
    }
}
