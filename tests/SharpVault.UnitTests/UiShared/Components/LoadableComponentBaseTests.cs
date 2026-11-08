// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Users;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class LoadableComponentBaseTests : MudTestContext
{
    private const string Page = "http://localhost/admin/users";

    private readonly ICurrentUserAccessor _accessor = Substitute.For<ICurrentUserAccessor>();
    private readonly CapturingLoggerProvider _logs = new();

    public LoadableComponentBaseTests()
    {
        Services.AddSingleton(_accessor);
        Services.AddLogging(builder => builder.AddProvider(_logs));
    }

    private static Func<CancellationToken, Task<ApiResponse<string>>> Returns(ApiResponse<string> response) =>
        _ => Task.FromResult(response);

    private IRenderedComponent<LoadHost> RenderHost(Func<CancellationToken, Task<ApiResponse<string>>> loader) =>
        Render<LoadHost>(parameters => parameters.Add(host => host.Loader, loader));

    [Fact]
    public void Init_LoadSucceeds_ShowsLoadedValue()
    {
        var cut = RenderHost(Returns(ApiResponse<string>.Success("value-one")));

        Assert.Equal("Loaded", cut.Find("#probe-state").TextContent);
        Assert.Equal("value-one", cut.Find("#probe-value").TextContent);
    }

    [Fact]
    public void Init_LoadPending_ShowsLoading()
    {
        var gate = new TaskCompletionSource<ApiResponse<string>>();

        var cut = RenderHost(_ => gate.Task);

        Assert.Equal("Loading", cut.Find("#probe-state").TextContent);
        Assert.NotNull(cut.Find("#probe-loading"));
    }

    [Fact]
    public void Init_EmptyValue_ShowsEmpty()
    {
        var cut = RenderHost(Returns(ApiResponse<string>.Success(string.Empty)));

        Assert.Equal("Empty", cut.Find("#probe-state").TextContent);
        Assert.NotNull(cut.Find("#probe-empty"));
    }

    [Fact]
    public void Init_Failed_ShowsErrorAndRetryReloads()
    {
        var calls = 0;
        var cut = RenderHost(_ => Task.FromResult(++calls == 1 ? ApiResponse<string>.Failed() : ApiResponse<string>.Success("second")));

        Assert.Equal("Error", cut.Find("#probe-state").TextContent);
        Assert.Contains(LoadableComponentBase<string>.GenericErrorText, cut.Find("#probe-error").TextContent, StringComparison.Ordinal);

        cut.Find("#probe-retry").Click();

        cut.WaitForAssertion(() => Assert.Equal("second", cut.Find("#probe-value").TextContent));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Init_SignInRequired_NavigatesToSignInWithEscapedReturnUrlAndRetryFlag()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(Page + "?filter=a b&x=1");

        RenderHost(Returns(ApiResponse<string>.SignInRequired()));

        var expectedReturn = Uri.EscapeDataString("/admin/users?filter=a%20b&x=1&" + AccountRoutes.SignInRetryParameter + "=1");
        var fake = Assert.IsType<Bunit.TestDoubles.BunitNavigationManager>(navigation);
        var last = fake.History.First();
        Assert.Equal($"{AccountRoutes.SignIn}?{AccountRoutes.ReturnUrlParameter}={expectedReturn}", last.Uri);
        Assert.True(last.Options.ForceLoad);
    }

    [Fact]
    public void Init_SignInRequiredWithFragment_DropsTheFragment()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(Page + "#section");

        RenderHost(Returns(ApiResponse<string>.SignInRequired()));

        var fake = Assert.IsType<Bunit.TestDoubles.BunitNavigationManager>(navigation);
        Assert.DoesNotContain("section", fake.History.First().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Init_SignInRequiredWithRetryFlag_ShowsErrorAndDoesNotNavigate()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"{Page}?{AccountRoutes.SignInRetryParameter}=1");
        var fake = Assert.IsType<Bunit.TestDoubles.BunitNavigationManager>(navigation);
        var historyBefore = fake.History.Count;

        var cut = RenderHost(Returns(ApiResponse<string>.SignInRequired()));

        Assert.Equal("Error", cut.Find("#probe-state").TextContent);
        Assert.Contains(LoadableComponentBase<string>.SignInLoopText, cut.Find("#probe-error").TextContent, StringComparison.Ordinal);
        Assert.Equal(historyBefore, fake.History.Count);
    }

    [Fact]
    public void Init_AccessDenied_NavigatesToAccessDeniedAndInvalidatesTheAccessor()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(Page);

        RenderHost(Returns(ApiResponse<string>.AccessDenied()));

        var fake = Assert.IsType<Bunit.TestDoubles.BunitNavigationManager>(navigation);
        var last = fake.History.First();
        Assert.Equal(AccountRoutes.AccessDenied, last.Uri);
        Assert.True(last.Options.ForceLoad);
        _accessor.Received(1).Invalidate();
    }

    [Theory]
    [InlineData(ApiCallStatus.NotFound, LoadableComponentBase<string>.NotFoundText)]
    [InlineData(ApiCallStatus.RateLimited, LoadableComponentBase<string>.RateLimitedText)]
    [InlineData(ApiCallStatus.ConfigurationMismatch, LoadableComponentBase<string>.ConfigurationMismatchText)]
    public void Load_Status_ShowsItsOwnText(ApiCallStatus status, string expected)
    {
        var cut = RenderHost(Returns(new ApiResponse<string>(status, null)));

        Assert.Equal("Error", cut.Find("#probe-state").TextContent);
        Assert.Contains(expected, cut.Find("#probe-error").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ApiCallStatus.Conflict)]
    [InlineData(ApiCallStatus.Invalid)]
    public void Load_ConflictOrInvalid_ShowsTheGenericTextWithoutServerDetail(ApiCallStatus status)
    {
        var cut = RenderHost(Returns(new ApiResponse<string>(status, null, "server detail 4711")));

        Assert.Contains(LoadableComponentBase<string>.GenericErrorText, cut.Find("#probe-error").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("4711", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Init_LoadThrows_ShowsGenericErrorWithoutExceptionText()
    {
        var cut = RenderHost(_ => throw new InvalidOperationException("secret connection detail"));

        Assert.Equal("Error", cut.Find("#probe-state").TextContent);
        Assert.DoesNotContain("secret connection detail", cut.Markup, StringComparison.Ordinal);
        Assert.Contains(LoadableComponentBase<string>.GenericErrorText, cut.Find("#probe-error").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("secret connection detail", _logs.AllText, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), _logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_DuringLoad_CancelsAndDoesNotSetState()
    {
        var gate = new TaskCompletionSource<ApiResponse<string>>();
        CancellationToken observed = default;
        var cut = RenderHost(token =>
        {
            observed = token;
            return gate.Task;
        });

        await cut.Instance.DisposeAsync();
        gate.SetResult(ApiResponse<string>.Success("late"));

        Assert.True(observed.IsCancellationRequested);
        Assert.Equal("Loading", cut.Instance.CurrentState);
    }

    [Fact]
    public void Load_UsesTheComponentTokenNeverNone()
    {
        CancellationToken observed = CancellationToken.None;

        RenderHost(token =>
        {
            observed = token;
            return Task.FromResult(ApiResponse<string>.Success("x"));
        });

        Assert.True(observed.CanBeCanceled);
        Assert.NotEqual(CancellationToken.None, observed);
    }

    [Theory]
    [InlineData(ApiCallStatus.Success)]
    [InlineData(ApiCallStatus.RateLimited)]
    public void TryHandleSessionStatus_OtherStatus_ReturnsFalseAndDoesNothing(ApiCallStatus status)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(Page);
        var cut = RenderHost(Returns(ApiResponse<string>.Success("value-one")));
        var fake = Assert.IsType<Bunit.TestDoubles.BunitNavigationManager>(navigation);
        var historyBefore = fake.History.Count;

        var handled = cut.Instance.TryHandle(status);

        Assert.False(handled);
        Assert.Equal(historyBefore, fake.History.Count);
        _accessor.DidNotReceive().Invalidate();
        Assert.Null(cut.Instance.CurrentError);
    }

    private sealed class LoadHost : LoadableComponentBase<string>
    {
        [Parameter]
        public Func<CancellationToken, Task<ApiResponse<string>>> Loader { get; set; } = _ => Task.FromResult(ApiResponse<string>.Failed());

        public string CurrentState => State.ToString();

        public string? CurrentError => ErrorText;

        public bool TryHandle(ApiCallStatus status) => TryHandleSessionStatus(status);

        protected override bool IsEmpty(string value) => value.Length == 0;

        protected override Task<ApiResponse<string>> LoadAsync(CancellationToken cancellationToken) => Loader(cancellationToken);

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "id", "probe-state");
            builder.AddContent(2, State.ToString());
            builder.CloseElement();
            builder.OpenComponent<DataStateView<string>>(3);
            builder.AddAttribute(4, nameof(DataStateView<string>.IdPrefix), "probe");
            builder.AddAttribute(5, nameof(DataStateView<string>.State), State);
            builder.AddAttribute(6, nameof(DataStateView<string>.Value), Value);
            builder.AddAttribute(7, nameof(DataStateView<string>.ErrorText), ErrorText);
            builder.AddAttribute(8, nameof(DataStateView<string>.OnRetry), EventCallback.Factory.Create(this, ReloadAsync));
            builder.AddAttribute(9, nameof(DataStateView<string>.ChildContent), (RenderFragment<string>)(value => inner =>
            {
                inner.OpenElement(0, "span");
                inner.AddAttribute(1, "id", "probe-value");
                inner.AddContent(2, value);
                inner.CloseElement();
            }));
            builder.CloseComponent();
        }
    }
}
