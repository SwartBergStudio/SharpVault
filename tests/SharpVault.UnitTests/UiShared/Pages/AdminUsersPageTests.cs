// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Users;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Pages;

public sealed class AdminUsersPageTests : ShellTestContext
{
    private static readonly DateTimeOffset SignedInAt = new(2026, 11, 4, 22, 47, 31, TimeSpan.FromHours(2));

    private static UserSummaryDto Summary(
        string name,
        UserStatusDto status = UserStatusDto.Active,
        DateTimeOffset? lastSignIn = null,
        string? email = null) =>
        new()
        {
            UserId = Guid.NewGuid(),
            DisplayName = name,
            Email = email ?? $"{name.ToLowerInvariant().Replace(' ', '.')}@sharpvault.invalid",
            Status = status,
            LastSignInAt = lastSignIn ?? SignedInAt,
        };

    private static UserPageDto Page(string? continuation, params UserSummaryDto[] users) =>
        new() { Users = [.. users], ContinuationToken = continuation };

    private void ReturnsPage(UserPageDto page) =>
        UsersClient.ListUsersAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(page)));

    private void SignInAsAdmin() => SignInAs("Admin One", isAdmin: true);

    [Fact]
    public async Task AdminUsersPage_ShowCounts_OpensTheDialogWithTheCountsOfThatUser()
    {
        SignInAsAdmin();
        var target = Summary("User Two");
        ReturnsPage(Page(null, target));
        UsersClient.GetCountsAsync(target.UserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserCountsDto>.Success(new UserCountsDto { ItemCount = 4, ShareCount = 2 })));
        var dialogs = RenderDialogProviders();
        var cut = Render<AdminUsers>();

        await cut.Find($"#user-counts-{target.UserId:D}").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("4", dialogs.Find("#user-counts-secrets").TextContent));
        Assert.Equal("2", dialogs.Find("#user-counts-shares").TextContent);
    }

    [Fact]
    public void AdminUsersPage_Admin_ListsUsersWithStatusAndNeverForEpoch()
    {
        SignInAsAdmin();
        ReturnsPage(Page(
            null,
            Summary("Admin One", lastSignIn: SignedInAt),
            Summary("User Two", lastSignIn: DateTimeOffset.UnixEpoch),
            Summary("User Three", UserStatusDto.Locked, SignedInAt)));

        var cut = Render<AdminUsers>();

        var rows = cut.FindAll("#admin-users-table tbody tr");
        Assert.Equal(3, rows.Count);
        Assert.Contains("Admin One", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("2026-11-04 20:47 UTC", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Never", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Active", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Locked", rows[2].TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#admin-users-forbidden"));
        Assert.Empty(cut.FindAll("#admin-users-load-more"));
    }

    [Fact]
    public void AdminUsersPage_LastSignInAfterEpoch_IsUtcWithMinutePrecision()
    {
        SignInAsAdmin();
        ReturnsPage(Page(null, Summary("Late One", lastSignIn: DateTimeOffset.UnixEpoch.AddSeconds(1))));

        var cut = Render<AdminUsers>();

        Assert.Contains("1970-01-01 00:00", cut.Find("#admin-users-table tbody tr").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AdminUsersPage_NonAdmin_ShowsAdminsOnlyPanelAndNeverCallsTheApi()
    {
        SignInAs("User One", isAdmin: false);

        var cut = Render<AdminUsers>();

        var panel = cut.Find("#admin-users-forbidden");
        Assert.Contains("Administrators only", panel.TextContent, StringComparison.Ordinal);
        Assert.Equal("alert", panel.GetAttribute("role"));
        Assert.Empty(cut.FindAll("#admin-users-table"));
        Assert.Empty(cut.FindAll("#admin-users-refresh"));
        _ = UsersClient.DidNotReceive().ListUsersAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdminUsersPage_EmptyList_ShowsEmptyState()
    {
        SignInAsAdmin();
        ReturnsPage(Page(null));

        var cut = Render<AdminUsers>();

        Assert.NotNull(cut.Find("#admin-users-empty"));
        Assert.Empty(cut.FindAll("#admin-users-table"));
        Assert.Empty(cut.FindAll("#admin-users-forbidden"));
    }

    [Fact]
    public void AdminUsersPage_ApiFailure_ShowsErrorWithRetry()
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Failed()));

        var cut = Render<AdminUsers>();

        var error = cut.Find("#admin-users-error");
        Assert.Contains(LoadableComponentBase<UserPageDto>.GenericErrorText, error.TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#admin-users-retry"));
        Assert.Empty(cut.FindAll("#admin-users-table"));
    }

    [Fact]
    public void AdminUsersPage_RetryAfterFailure_ListsTheUsers()
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(ApiResponse<UserPageDto>.Failed()),
            Task.FromResult(ApiResponse<UserPageDto>.Success(Page(null, Summary("Admin One")))));
        var cut = Render<AdminUsers>();

        cut.Find("#admin-users-retry").Click();

        Assert.Single(cut.FindAll("#admin-users-table tbody tr"));
    }

    [Fact]
    public void AdminUsersPage_CurrentUserUnavailable_ShowsErrorAndNeverCallsTheUsersApi()
    {
        Authorization.SetAuthorized("Admin One");
        ReturnsCurrentUserStatus(ApiResponse<CurrentUserDto>.Failed());

        var cut = Render<AdminUsers>();

        Assert.NotNull(cut.Find("#admin-users-error"));
        _ = UsersClient.DidNotReceive().ListUsersAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdminUsersPage_FirstLoad_RequestsTheFirstPageOfFiftyWithoutToken()
    {
        SignInAsAdmin();
        ReturnsPage(Page(null, Summary("Admin One")));

        Render<AdminUsers>();

        _ = UsersClient.Received(1).ListUsersAsync(50, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdminUsersPage_LoadMore_AppendsRowsAndPassesTheContinuationToken()
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page("token-two", Summary("Admin One")))));
        UsersClient.ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page(null, Summary("User Two")))));
        var cut = Render<AdminUsers>();
        Assert.NotNull(cut.Find("#admin-users-load-more"));

        cut.Find("#admin-users-load-more").Click();

        Assert.Equal(2, cut.FindAll("#admin-users-table tbody tr").Count);
        Assert.Empty(cut.FindAll("#admin-users-load-more"));
        _ = UsersClient.Received(1).ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdminUsersPage_LoadMore_StopsAtThousandRows()
    {
        SignInAsAdmin();
        var firstPage = Enumerable.Range(0, 980).Select(index => Summary($"User {index}")).ToArray();
        var secondPage = Enumerable.Range(0, 50).Select(index => Summary($"Extra {index}")).ToArray();
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page("more", firstPage))));
        UsersClient.ListUsersAsync(50, "more", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page("even-more", secondPage))));
        var cut = Render<AdminUsers>();

        cut.Find("#admin-users-load-more").Click();

        Assert.Equal(UserDirectoryTable.MaxRowsLoaded, cut.FindAll("#admin-users-table tbody tr").Count);
        Assert.Empty(cut.FindAll("#admin-users-load-more"));
        Assert.Contains("1000", cut.Find("#admin-users-limit").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminUsersPage_LoadMoreFails_KeepsRowsAndShowsAnInlineError()
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page("token-two", Summary("Admin One")))));
        UsersClient.ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Failed()));
        var cut = Render<AdminUsers>();

        cut.Find("#admin-users-load-more").Click();

        Assert.Single(cut.FindAll("#admin-users-table tbody tr"));
        Assert.NotNull(cut.Find("#admin-users-load-more-error"));
        Assert.NotNull(cut.Find("#admin-users-load-more"));
    }

    [Theory]
    [InlineData(ApiCallStatus.RateLimited)]
    [InlineData(ApiCallStatus.NotFound)]
    public void AdminUsersPage_LoadMoreRateLimitedOrNotFound_KeepsRowsAndShowsAnInlineError(ApiCallStatus status)
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page("token-two", Summary("Admin One")))));
        UsersClient.ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ApiResponse<UserPageDto>(status, null)));
        var cut = Render<AdminUsers>();

        cut.Find("#admin-users-load-more").Click();

        Assert.Single(cut.FindAll("#admin-users-table tbody tr"));
        Assert.Empty(cut.FindAll("#admin-users-error"));
        Assert.NotNull(cut.Find("#admin-users-load-more-error"));
        Assert.NotNull(cut.Find("#admin-users-load-more"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AdminUsersPage_DisplayNameAndEmailWithMarkup_AreEncoded()
    {
        SignInAsAdmin();
        const string hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        ReturnsPage(Page(null, Summary(hostile, email: hostile)));

        var cut = Render<AdminUsers>();

        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("<img", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("&lt;img", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AdminUsersPage_UnknownStatus_IsRenderedAsLockedNotActive()
    {
        SignInAsAdmin();
        ReturnsPage(Page(null, Summary("Odd One", (UserStatusDto)99)));

        var cut = Render<AdminUsers>();

        var row = cut.Find("#admin-users-table tbody tr");
        Assert.Contains("Locked", row.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Active", row.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminUsersPage_Refresh_ReloadsFromTheFirstPage()
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(ApiResponse<UserPageDto>.Success(Page("token-two", Summary("Admin One")))),
            Task.FromResult(ApiResponse<UserPageDto>.Success(Page(null, Summary("Fresh One"), Summary("Fresh Two")))));
        UsersClient.ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page(null, Summary("User Two")))));
        var cut = Render<AdminUsers>();
        cut.Find("#admin-users-load-more").Click();
        Assert.Equal(2, cut.FindAll("#admin-users-table tbody tr").Count);

        cut.Find("#admin-users-refresh").Click();

        var rows = cut.FindAll("#admin-users-table tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Fresh One", rows[0].TextContent, StringComparison.Ordinal);
        _ = UsersClient.Received(2).ListUsersAsync(50, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdminUsersPage_SignInRequired_NavigatesToSignIn()
    {
        Authorization.SetAuthorized("Admin One");
        ReturnsCurrentUserStatus(ApiResponse<CurrentUserDto>.SignInRequired());

        var cut = Render<AdminUsers>();

        Assert.Contains("/account/sign-in", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#admin-users-table"));
    }

    [Fact]
    public void AdminUsersPage_LoadMoreSignInRequired_RedirectsOnceAndNoRetryLoop()
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page("token-two", Summary("Admin One")))));
        UsersClient.ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.SignInRequired()));
        var cut = Render<AdminUsers>();

        cut.Find("#admin-users-load-more").Click();

        var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>());
        Assert.Single(navigation.History, entry => entry.Uri.Contains("/account/sign-in", StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#admin-users-load-more-error"));
        _ = UsersClient.Received(1).ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdminUsersPage_LoadMoreAccessDenied_GoesToAccessDenied()
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page("token-two", Summary("Admin One")))));
        UsersClient.ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.AccessDenied()));
        var cut = Render<AdminUsers>();

        cut.Find("#admin-users-load-more").Click();

        var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>());
        Assert.Contains(navigation.History, entry => entry.Uri == "/account/access-denied" && entry.Options.ForceLoad);
        Accessor.Received().Invalidate();
        Assert.Empty(cut.FindAll("#admin-users-table tbody tr"));
    }

    [Fact]
    public void AdminUsersPage_Load_MakesNoCountsCalls()
    {
        SignInAsAdmin();
        ReturnsPage(Page(null, Summary("User Two")));

        Render<AdminUsers>();

        _ = UsersClient.DidNotReceive().GetCountsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AdminUsersPage_LoadMore_MakesNoCountsCalls()
    {
        SignInAsAdmin();
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page("token-two", Summary("Admin One")))));
        UsersClient.ListUsersAsync(50, "token-two", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(Page(null, Summary("User Two")))));
        var cut = Render<AdminUsers>();

        cut.Find("#admin-users-load-more").Click();

        Assert.Equal(2, cut.FindAll("#admin-users-table tbody tr").Count);
        _ = UsersClient.DidNotReceive().GetCountsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AdminUsersPage_OwnRow_ShowsCountsButNotLock()
    {
        var caller = Guid.NewGuid();
        Authorization.SetAuthorized("Admin One");
        ReturnsCurrentUser(new CurrentUserDto { UserId = caller, DisplayName = "Admin One", IsAdmin = true });
        var own = Summary("Admin One");
        own.UserId = caller;
        var other = Summary("User Two");
        ReturnsPage(Page(null, own, other));

        var cut = Render<AdminUsers>();

        Assert.NotNull(cut.Find($"#user-counts-{caller:D}"));
        Assert.Empty(cut.FindAll($"#user-lock-{caller:D}"));
        Assert.Empty(cut.FindAll($"#user-unlock-{caller:D}"));
        Assert.Empty(cut.FindAll($"#user-transfer-{caller:D}"));
        Assert.NotNull(cut.Find($"#user-lock-{other.UserId:D}"));
    }

    [Fact]
    public async Task AdminUsersPage_CloseCounts_RemovesThePanel()
    {
        SignInAsAdmin();
        var target = Summary("User Two");
        ReturnsPage(Page(null, target));
        ReturnsCounts(target.UserId, Counts(items: 4, shares: 2));
        var dialogs = RenderDialogProviders();
        var cut = Render<AdminUsers>();
        await cut.Find($"#user-counts-{target.UserId:D}").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("4", dialogs.Find("#user-counts-secrets").TextContent));

        await dialogs.Find("#user-counts-close").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await WaitForClosedDialogsToSettleAsync();

        Assert.Empty(dialogs.FindAll("#user-counts-secrets"));
    }

    [Fact]
    public async Task AdminUsersPage_CountsForAnotherUser_MakesAFreshCall()
    {
        SignInAsAdmin();
        var first = Summary("User Two");
        var second = Summary("User Three");
        ReturnsPage(Page(null, first, second));
        ReturnsCounts(first.UserId, Counts(items: 4, shares: 2));
        ReturnsCounts(second.UserId, Counts(items: 9, shares: 7));
        var dialogs = RenderDialogProviders();
        var cut = Render<AdminUsers>();
        await cut.Find($"#user-counts-{first.UserId:D}").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal("4", dialogs.Find("#user-counts-secrets").TextContent));
        await dialogs.Find("#user-counts-close").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await WaitForClosedDialogsToSettleAsync();

        await cut.Find($"#user-counts-{second.UserId:D}").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("9", dialogs.Find("#user-counts-secrets").TextContent));
        _ = UsersClient.Received(1).GetCountsAsync(first.UserId, Arg.Any<CancellationToken>());
        _ = UsersClient.Received(1).GetCountsAsync(second.UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdminUsersPage_CountsFailed_ShowsTheErrorInsideTheDialog()
    {
        SignInAsAdmin();
        var target = Summary("User Two");
        ReturnsPage(Page(null, target));
        ReturnsCounts(target.UserId, ApiResponse<UserCountsDto>.Failed());
        var dialogs = RenderDialogProviders();
        var cut = Render<AdminUsers>();

        await cut.Find($"#user-counts-{target.UserId:D}").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains(LoadableComponentBase<UserCountsDto>.GenericErrorText, dialogs.Find("#user-counts-error").TextContent, StringComparison.Ordinal));
        Assert.NotNull(dialogs.Find("#user-counts-retry"));
        Assert.Empty(dialogs.FindAll("#user-counts-secrets"));
    }

    [Fact]
    public async Task AdminUsersPage_CountsCapped_ShowsTheWarningInsideTheDialog()
    {
        SignInAsAdmin();
        var target = Summary("User Two");
        ReturnsPage(Page(null, target));
        ReturnsCounts(target.UserId, Counts(items: 1000, shares: 3, capped: true));
        var dialogs = RenderDialogProviders();
        var cut = Render<AdminUsers>();

        await cut.Find($"#user-counts-{target.UserId:D}").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("very large", dialogs.Find("#user-counts-capped").TextContent, StringComparison.Ordinal));
        Assert.Equal("1000", dialogs.Find("#user-counts-secrets").TextContent);
    }

    private void ReturnsCounts(Guid userId, UserCountsDto counts) =>
        UsersClient.GetCountsAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserCountsDto>.Success(counts)));

    private void ReturnsCounts(Guid userId, ApiResponse<UserCountsDto> response) =>
        UsersClient.GetCountsAsync(userId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(response));

    private static UserCountsDto Counts(int items, int shares, bool capped = false) =>
        new() { ItemCount = items, ShareCount = shares, CountsAreCapped = capped };
}
