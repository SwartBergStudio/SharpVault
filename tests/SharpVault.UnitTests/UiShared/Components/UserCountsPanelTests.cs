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

public sealed class UserCountsPanelTests : ShellTestContext
{
    private const string AccessDeniedUrl = "/account/access-denied";

    private static UserCountsDto Counts(int items = 4, int shares = 2, bool capped = false) =>
        new() { ItemCount = items, ShareCount = shares, CountsAreCapped = capped };

    private IRenderedComponent<UserCountsPanel> RenderFor(Guid userId) =>
        Render<UserCountsPanel>(parameters => parameters.Add(panel => panel.UserId, userId));

    private void ReturnsCounts(Guid userId, ApiResponse<UserCountsDto> response) =>
        UsersClient.GetCountsAsync(userId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(response));

    private BunitNavigationManager Navigation() =>
        Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());

    [Fact]
    public void UserCountsPanel_WhileLoading_ShowsTheBusyIndicator()
    {
        var userId = Guid.NewGuid();
        UsersClient.GetCountsAsync(userId, Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<ApiResponse<UserCountsDto>>().Task);

        var cut = RenderFor(userId);

        Assert.NotNull(cut.Find("#user-counts-loading"));
        Assert.Empty(cut.FindAll("#user-counts-secrets"));
        Assert.Empty(cut.FindAll("#user-counts-error"));
    }

    [Fact]
    public void UserCountsPanel_Loaded_ShowsTheSecretAndShareCounts()
    {
        var userId = Guid.NewGuid();
        ReturnsCounts(userId, ApiResponse<UserCountsDto>.Success(Counts(items: 4, shares: 2)));

        var cut = RenderFor(userId);

        Assert.Equal("4", cut.Find("#user-counts-secrets").TextContent);
        Assert.Equal("2", cut.Find("#user-counts-shares").TextContent);
        Assert.Contains("Shares on their items", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#user-counts-loading"));
    }

    [Fact]
    public void UserCountsPanel_Capped_ShowsTheWarning()
    {
        var userId = Guid.NewGuid();
        ReturnsCounts(userId, ApiResponse<UserCountsDto>.Success(Counts(items: 120_000, shares: 9, capped: true)));

        var cut = RenderFor(userId);

        Assert.Contains("very large", cut.Find("#user-counts-capped").TextContent, StringComparison.Ordinal);
        Assert.Equal("120000", cut.Find("#user-counts-secrets").TextContent);
    }

    [Fact]
    public void UserCountsPanel_NotCapped_ShowsNoWarning()
    {
        var userId = Guid.NewGuid();
        ReturnsCounts(userId, ApiResponse<UserCountsDto>.Success(Counts(capped: false)));

        var cut = RenderFor(userId);

        Assert.Empty(cut.FindAll("#user-counts-capped"));
    }

    [Fact]
    public void UserCountsPanel_Failed_ShowsTheErrorAndRetryWorks()
    {
        var userId = Guid.NewGuid();
        UsersClient.GetCountsAsync(userId, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(ApiResponse<UserCountsDto>.Failed()),
            Task.FromResult(ApiResponse<UserCountsDto>.Success(Counts(items: 3, shares: 1))));

        var cut = RenderFor(userId);

        Assert.Contains(LoadableComponentBase<UserCountsDto>.GenericErrorText, cut.Find("#user-counts-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#user-counts-secrets"));

        cut.Find("#user-counts-retry").Click();

        cut.WaitForAssertion(() => Assert.Equal("3", cut.Find("#user-counts-secrets").TextContent));
        Assert.Empty(cut.FindAll("#user-counts-error"));
        _ = UsersClient.Received(2).GetCountsAsync(userId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void UserCountsPanel_NotFound_ShowsTheNotFoundText()
    {
        var userId = Guid.NewGuid();
        ReturnsCounts(userId, ApiResponse<UserCountsDto>.NotFound());

        var cut = RenderFor(userId);

        Assert.Contains(LoadableComponentBase<UserCountsDto>.NotFoundText, cut.Find("#user-counts-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#user-counts-secrets"));
    }

    [Fact]
    public void UserCountsPanel_RateLimited_ShowsTheRateLimitedText()
    {
        var userId = Guid.NewGuid();
        ReturnsCounts(userId, ApiResponse<UserCountsDto>.RateLimited());

        var cut = RenderFor(userId);

        Assert.Contains(LoadableComponentBase<UserCountsDto>.RateLimitedText, cut.Find("#user-counts-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#user-counts-secrets"));
    }

    [Fact]
    public void UserCountsPanel_Throws_ShowsTheGenericErrorText()
    {
        var userId = Guid.NewGuid();
        UsersClient.GetCountsAsync(userId, Arg.Any<CancellationToken>())
            .Returns<Task<ApiResponse<UserCountsDto>>>(_ => throw new HttpRequestException("The network is down."));

        var cut = RenderFor(userId);

        Assert.Contains(LoadableComponentBase<UserCountsDto>.GenericErrorText, cut.Find("#user-counts-error").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#user-counts-secrets"));
    }

    [Fact]
    public void UserCountsPanel_SignInRequired_Redirects()
    {
        var userId = Guid.NewGuid();
        ReturnsCounts(userId, ApiResponse<UserCountsDto>.SignInRequired());

        var cut = RenderFor(userId);

        Assert.Single(Navigation().History, entry => entry.Uri.Contains("/account/sign-in", StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#user-counts-secrets"));
    }

    [Fact]
    public void UserCountsPanel_AccessDenied_GoesToAccessDenied()
    {
        var userId = Guid.NewGuid();
        ReturnsCounts(userId, ApiResponse<UserCountsDto>.AccessDenied());

        var cut = RenderFor(userId);

        Assert.Contains(Navigation().History, entry => entry.Uri == AccessDeniedUrl && entry.Options.ForceLoad);
        Accessor.Received().Invalidate();
        Assert.Empty(cut.FindAll("#user-counts-secrets"));
    }
}
