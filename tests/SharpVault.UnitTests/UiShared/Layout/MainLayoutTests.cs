// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Layout;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Layout;

public sealed class MainLayoutTests : ShellTestContext
{
    private const string LeakMarker = "SV-EXCEPTION-DETAIL-4471";

    public MainLayoutTests()
    {
        SetRendererInfo(new RendererInfo("Server", true));
    }

    private IRenderedComponent<MainLayout> RenderLayout(RenderFragment? body = null) =>
        Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, body ?? PageBody()));

    private static RenderFragment PageBody() =>
        builder => builder.AddMarkupContent(0, "<p id=\"page-body\">Page body</p>");

    [Fact]
    public void MainLayout_SignedOut_HasSkipLinkHeaderMainAndNoNav()
    {
        var cut = RenderLayout();

        Assert.NotNull(cut.Find("a#skip-to-content"));
        Assert.NotNull(cut.Find("header#app-header"));
        var main = cut.Find("main#main-content");
        Assert.Equal("-1", main.GetAttribute("tabindex"));
        Assert.Equal("Page body", cut.Find("main#main-content #app-shell #page-body").TextContent);
        Assert.Single(cut.FindAll("main"));
        Assert.Empty(cut.FindAll("#side-nav"));
        Assert.Empty(cut.FindAll("#nav-toggle"));
    }

    [Fact]
    public void MainLayout_SignedIn_ShowsNavigationAndMenuToggle()
    {
        SignInAs(DisplayName, isAdmin: false);

        var cut = RenderLayout();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#side-nav")));
        Assert.NotNull(cut.Find("#nav-toggle"));
        Assert.Empty(cut.FindAll("#nav-admin-users"));
    }

    [Fact]
    public void MainLayout_SignedInAdmin_ShowsAdminLinkFromTheApi()
    {
        SignInAs(DisplayName, isAdmin: true);

        var cut = RenderLayout();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#nav-admin-users")));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_PrincipalChanges_ReloadsTheCurrentUserAndNavigation()
    {
        SignInAs("Admin One", isAdmin: true);
        var cut = RenderLayout();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#nav-admin-users")));
        Accessor.ClearReceivedCalls();

        ReturnsCurrentUser(new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = "User One", IsAdmin = false });
        Authorization.SetAuthorized("User One");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#nav-admin-users")));
        cut.WaitForAssertion(() => Assert.Equal("User One", cut.Find("#account-name").TextContent));
        _ = Accessor.Received(1).GetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_CurrentUserLoadFails_HidesAdminLinkAndStillRenders()
    {
        Authorization.SetAuthorized(DisplayName);
        Accessor.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<ApiResponse<CurrentUserDto>>(new HttpRequestException(LeakMarker)));

        var cut = RenderLayout();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#side-nav")));
        Assert.Empty(cut.FindAll("#nav-admin-users"));
        Assert.Equal("Page body", cut.Find("#page-body").TextContent);
        Assert.DoesNotContain(LeakMarker, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(LogMessages, message => message.Contains(LeakMarker, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_CurrentUserAccessDenied_HidesAdminLink()
    {
        Authorization.SetAuthorized(DisplayName);
        ReturnsCurrentUserStatus(ApiResponse<CurrentUserDto>.AccessDenied());

        var cut = RenderLayout();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#side-nav")));
        Assert.Empty(cut.FindAll("#nav-admin-users"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_Rendered_HostsTheIdleLock()
    {
        var cut = RenderLayout();

        Assert.NotNull(cut.FindComponent<IdleLock>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_SignedIn_StartsTheIdleMonitorThroughTheLayout()
    {
        SignInAs(DisplayName, isAdmin: false);

        RenderLayout();

        IdleMonitor.Received(1).StartAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MainLayout_SignedOut_NeverCallsTheCurrentUserApi()
    {
        RenderLayout();

        Accessor.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void MainLayout_StaticRender_NeverCallsTheCurrentUserApi()
    {
        SetRendererInfo(new RendererInfo("Static", false));
        Authorization.SetAuthorized(DisplayName);

        var cut = RenderLayout();

        Accessor.DidNotReceive().GetAsync(Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("#nav-toggle"));
        Assert.Empty(cut.FindAll("#nav-admin-users"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_Rendered_HasUniqueIds()
    {
        SignInAs(DisplayName, isAdmin: true);

        var cut = RenderLayout();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#nav-admin-users")));

        AssertUniqueIds(cut);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_SignedOut_HasUniqueIds()
    {
        var cut = RenderLayout();

        AssertUniqueIds(cut);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_Rendered_ContainsNoScriptOrEventHandlerAttributes()
    {
        SignInAs(DisplayName, isAdmin: true);

        var cut = RenderLayout();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#nav-admin-users")));

        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("javascript:", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cut.FindAll("*"), element => element.Attributes.Any(attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void MainLayout_Rendered_ErrorUiDismissIsLabelledButton()
    {
        var cut = RenderLayout();

        var errorUi = cut.Find("#blazor-error-ui");
        Assert.Equal("alert", errorUi.GetAttribute("role"));
        var dismiss = cut.Find("#blazor-error-dismiss");
        Assert.Equal("button", dismiss.LocalName);
        Assert.Equal("button", dismiss.GetAttribute("type"));
        Assert.Contains("dismiss", dismiss.ClassList);
        Assert.Equal("Dismiss", dismiss.GetAttribute("aria-label"));
        Assert.Contains("reload", cut.Find("#blazor-error-reload").ClassList);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_ChildThrows_ShowsErrorBoundaryPanelWithoutExceptionText()
    {
        var cut = RenderLayout(ThrowingBody());

        var panel = cut.Find("#error-boundary");
        Assert.Equal("alert", panel.GetAttribute("role"));
        Assert.Equal("Something went wrong", cut.Find("#error-boundary-title").TextContent);
        Assert.NotNull(cut.Find("#error-boundary-retry"));
        Assert.Equal("/", cut.Find("#error-boundary-home").GetAttribute("href"));
        Assert.DoesNotContain(LeakMarker, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("header#app-header"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_ChildThrows_LogsOnlyTheExceptionTypeName()
    {
        RenderLayout(ThrowingBody());

        var logged = Assert.Single(LogMessages, message => message.Contains("InvalidOperationException", StringComparison.Ordinal));
        Assert.DoesNotContain(LeakMarker, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(LogMessages, message => message.Contains(LeakMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void MainLayout_ChildThrows_RetryRendersTheChildAgain()
    {
        var attempts = 0;
        var cut = RenderLayout(builder =>
        {
            builder.OpenComponent<FlakyComponent>(0);
            builder.AddAttribute(1, nameof(FlakyComponent.Attempt), (Func<int>)(() => ++attempts));
            builder.CloseComponent();
        });
        Assert.NotNull(cut.Find("#error-boundary"));

        cut.Find("#error-boundary-retry").Click();

        Assert.Equal("Recovered", cut.Find("#flaky-ok").TextContent);
        Assert.Empty(cut.FindAll("#error-boundary"));
    }

    [Fact]
    public void MainLayout_BodyChanges_RecoversTheBoundary()
    {
        var cut = RenderLayout(ThrowingBody());
        Assert.NotNull(cut.Find("#error-boundary"));

        cut.Render(parameters => parameters.Add(layout => layout.Body, PageBody()));

        Assert.Empty(cut.FindAll("#error-boundary"));
        Assert.Equal("Page body", cut.Find("#page-body").TextContent);
    }

    private static RenderFragment ThrowingBody() =>
        builder =>
        {
            builder.OpenComponent<ThrowingComponent>(0);
            builder.CloseComponent();
        };

    private static void AssertUniqueIds(IRenderedComponent<MainLayout> cut)
    {
        var ids = cut.FindAll("[id]").Select(element => element.Id).ToList();
        Assert.NotEmpty(ids);
        var duplicates = ids.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        Assert.True(duplicates.Count == 0, $"Duplicate ids: {string.Join(", ", duplicates)}");
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    private sealed class ThrowingComponent : ComponentBase
    {
        protected override void OnInitialized() => throw new InvalidOperationException(LeakMarker);
    }

    private sealed class FlakyComponent : ComponentBase
    {
        [Parameter]
        public Func<int> Attempt { get; set; } = () => 0;

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            if (Attempt() == 1)
            {
                throw new InvalidOperationException(LeakMarker);
            }

            builder.AddMarkupContent(0, "<p id=\"flaky-ok\">Recovered</p>");
        }
    }
}
