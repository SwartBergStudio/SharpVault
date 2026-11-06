// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Users;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

[Trait("Category", "Security")]
public sealed class UserDirectoryTableTests : ShellTestContext
{
    private const string Token = "token-two";

    private static UserSummaryDto Summary(string name) =>
        new() { UserId = Guid.NewGuid(), DisplayName = name, Email = $"{name.ToLowerInvariant().Replace(' ', '.')}@sharpvault.invalid", Status = UserStatusDto.Active, LastSignInAt = DateTimeOffset.UtcNow };

    private IRenderedComponent<UserDirectoryTable> RenderWithLoadMoreReturning(ApiResponse<UserPageDto> second)
    {
        SignInAs("Admin One", isAdmin: true);
        UsersClient.ListUsersAsync(50, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(new UserPageDto { Users = [Summary("Admin One"), Summary("User One")], ContinuationToken = Token })));
        UsersClient.ListUsersAsync(50, Token, Arg.Any<CancellationToken>()).Returns(Task.FromResult(second));
        var cut = Render<UserDirectoryTable>();
        Assert.Equal(2, cut.FindAll("#admin-users-table tbody tr").Count);
        cut.Find("#admin-users-load-more").Click();
        return cut;
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    [Fact]
    public void LoadMore_Success_AppendsRows()
    {
        var cut = RenderWithLoadMoreReturning(ApiResponse<UserPageDto>.Success(new UserPageDto { Users = [Summary("Extra One")] }));

        Assert.Equal(3, cut.FindAll("#admin-users-table tbody tr").Count);
        Assert.Empty(cut.FindAll("#admin-users-load-more-error"));
        Assert.Empty(cut.FindAll("#admin-users-load-more"));
    }

    [Fact]
    public void LoadMore_Failed_KeepsRowsAndShowsTheInlineErrorWithRetry()
    {
        var cut = RenderWithLoadMoreReturning(ApiResponse<UserPageDto>.Failed());

        Assert.Equal(2, cut.FindAll("#admin-users-table tbody tr").Count);
        Assert.NotNull(cut.Find("#admin-users-load-more-error"));
        Assert.NotNull(cut.Find("#admin-users-load-more"));
    }

    [Fact]
    public void LoadMore_AccessDenied_ClearsTheRowsAndNavigatesToAccessDenied()
    {
        var cut = RenderWithLoadMoreReturning(ApiResponse<UserPageDto>.AccessDenied());

        Assert.Empty(cut.FindAll("#admin-users-table tbody tr"));
        Assert.Empty(cut.FindAll("#admin-users-load-more"));
        Assert.Empty(cut.FindAll("#admin-users-load-more-error"));
        var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
        Assert.Equal("/account/access-denied", navigation.History.First().Uri);
        Assert.True(navigation.History.First().Options.ForceLoad);
        Accessor.Received().Invalidate();
    }

    [Fact]
    public void LoadMore_SignInRequired_NavigatesToSignInAndShowsNoInlineError()
    {
        var cut = RenderWithLoadMoreReturning(ApiResponse<UserPageDto>.SignInRequired());

        Assert.Contains("/account/sign-in", CurrentUri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#admin-users-load-more-error"));
    }

    [Fact]
    public void LoadMore_ConfigurationMismatch_ShowsTheMismatchErrorInsteadOfTheRows()
    {
        var cut = RenderWithLoadMoreReturning(ApiResponse<UserPageDto>.ConfigurationMismatch());

        Assert.Empty(cut.FindAll("#admin-users-table"));
        Assert.Contains(LoadableComponentBase<UserPageDto>.ConfigurationMismatchText, cut.Find("#admin-users-error").TextContent, StringComparison.Ordinal);
    }
}
