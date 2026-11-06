// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Users;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Pages;

[Trait("Category", "Security")]
public sealed partial class RenderTimeSecurityTests : ShellTestContext
{
    private const string TokenHeader = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9";
    private const string TokenPayload = "eyJzdWIiOiJzdi1yZW5kZXItcGF5bG9hZC03NzMxIn0";
    private const string TokenSignature = "sv-render-signature-9941";
    private const string LeakMarker = "SV-RENDER-LEAK-5520";
    private const string UserName = "Grace Hopper";
    private const string UserEmail = "grace.hopper@sharpvault.invalid";
    private const string ProfileJson = """{"userId":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","displayName":"Grace Hopper","isAdmin":true}""";
    private const string UsersJson = """{"users":[{"userId":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","displayName":"Grace Hopper","email":"grace.hopper@sharpvault.invalid","status":"Active","lastSignInAt":"2026-01-02T03:04:05+00:00"}],"continuationToken":null}""";

    private static readonly string Token = $"{TokenHeader}.{TokenPayload}.{TokenSignature}";

    private readonly StubHttpHandler _meHandler = new();
    private readonly StubHttpHandler _usersHandler = new();

    public RenderTimeSecurityTests()
    {
        var tokens = Substitute.For<IApiAccessTokenProvider>();
        tokens.InteractiveSignInAvailable.Returns(true);
        tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.Bearer(Token));
        var session = Substitute.For<ISessionValidator>();
        session.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(true);
        _meHandler.Respond(HttpStatusCode.OK, ProfileJson);
        _usersHandler.Respond(HttpStatusCode.OK, UsersJson);
        Services.AddSingleton<IMeApiClient>(sp => new MeApiClient(
            new HttpClient(_meHandler) { BaseAddress = new Uri("https://api.example.test/") },
            tokens,
            sp.GetRequiredService<AuthenticationStateProvider>(),
            session,
            sp.GetRequiredService<ILogger<MeApiClient>>()));
        Services.AddSingleton<IAdminUsersApiClient>(sp => new AdminUsersApiClient(
            new HttpClient(_usersHandler) { BaseAddress = new Uri("https://api.example.test/") },
            tokens,
            sp.GetRequiredService<AuthenticationStateProvider>(),
            session,
            sp.GetRequiredService<ILogger<AdminUsersApiClient>>()));
        Services.AddScoped<ICurrentUserAccessor>(sp => new CurrentUserAccessor(
            sp.GetRequiredService<IMeApiClient>(),
            sp.GetRequiredService<AuthenticationStateProvider>()));
        Authorization.SetAuthorized(UserName);
        SetRendererInfo(new RendererInfo("Server", true));
    }

    [GeneratedRegex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineEventHandlerPattern();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+.[A-Za-z0-9_-]+.", RegexOptions.CultureInvariant)]
    private static partial Regex JwtShapePattern();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _meHandler.Dispose();
            _usersHandler.Dispose();
        }

        base.Dispose(disposing);
    }

    private IRenderedComponent<Account> RenderAccount()
    {
        var cut = Render<Account>();
        cut.WaitForAssertion(() => Assert.Contains(UserName, cut.Markup, StringComparison.Ordinal));
        return cut;
    }

    private IRenderedComponent<AdminUsers> RenderAdminUsers()
    {
        var cut = Render<AdminUsers>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#admin-users-table")));
        return cut;
    }

    private void AssertNoTokenNameOrEmailLogged()
    {
        var logs = LogText;
        Assert.DoesNotContain(Token, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenHeader, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenPayload, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenSignature, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(UserName, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(UserEmail, logs, StringComparison.Ordinal);
    }

    private static void AssertMarkupHasNoTokenOrInlineHandlers(string markup)
    {
        Assert.DoesNotContain(TokenHeader, markup, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenPayload, markup, StringComparison.Ordinal);
        Assert.DoesNotContain(TokenSignature, markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer ", markup, StringComparison.Ordinal);
        Assert.DoesNotMatch(JwtShapePattern(), markup);
        Assert.DoesNotMatch(InlineEventHandlerPattern(), markup);
        Assert.DoesNotContain("javascript:", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Account_RenderedWithRealClient_LeaksNothingInMarkupOrLogs()
    {
        var cut = RenderAccount();

        AssertMarkupHasNoTokenOrInlineHandlers(cut.Markup);
        AssertNoTokenNameOrEmailLogged();
    }

    [Fact]
    public void AdminUsers_RenderedWithRealClient_LeaksNothingInMarkupOrLogs()
    {
        var cut = RenderAdminUsers();

        Assert.Contains(UserEmail, cut.Markup, StringComparison.Ordinal);
        AssertMarkupHasNoTokenOrInlineHandlers(cut.Markup);
        AssertNoTokenNameOrEmailLogged();
    }

    [Fact]
    public void ShellDom_SignedInWithRealClients_ContainsNoJwtLikeString()
    {
        var cut = Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, (RenderFragment)(builder => builder.AddMarkupContent(0, "<p>body</p>"))));
        cut.WaitForAssertion(() => Assert.Contains(UserName, cut.Find("#account-name").TextContent, StringComparison.Ordinal));

        AssertMarkupHasNoTokenOrInlineHandlers(cut.Markup);
        AssertNoTokenNameOrEmailLogged();
        Assert.Single(_meHandler.Requests);
    }

    [Fact]
    public void AdminUsers_SentTheBearerToken_ButNeverRendersIt()
    {
        var cut = RenderAdminUsers();

        var request = Assert.Single(_usersHandler.Requests);
        Assert.Equal(Token, request.AuthorizationParameter);
        AssertMarkupHasNoTokenOrInlineHandlers(cut.Markup);
    }

    [Fact]
    public void Account_ApiCallThrowsWithTheTokenInTheMessage_RendersNoLeakAndLogsOnlyTheType()
    {
        _meHandler.Throw(new HttpRequestException($"{LeakMarker} {Token}"));

        var cut = Render<Account>();

        cut.WaitForAssertion(() => Assert.Contains(nameof(HttpRequestException), LogText, StringComparison.Ordinal));
        Assert.DoesNotContain(LeakMarker, cut.Markup, StringComparison.Ordinal);
        AssertMarkupHasNoTokenOrInlineHandlers(cut.Markup);
        Assert.DoesNotContain(LeakMarker, LogText, StringComparison.Ordinal);
        AssertNoTokenNameOrEmailLogged();
    }

    [Fact]
    public void AdminUsers_ApiCallThrowsWithTheTokenInTheMessage_RendersNoLeakAndLogsOnlyTheType()
    {
        _usersHandler.Throw(new HttpRequestException($"{LeakMarker} {Token}"));

        var cut = Render<AdminUsers>();

        cut.WaitForAssertion(() => Assert.Contains(nameof(HttpRequestException), LogText, StringComparison.Ordinal));
        Assert.DoesNotContain(LeakMarker, cut.Markup, StringComparison.Ordinal);
        AssertMarkupHasNoTokenOrInlineHandlers(cut.Markup);
        Assert.DoesNotContain(LeakMarker, LogText, StringComparison.Ordinal);
        AssertNoTokenNameOrEmailLogged();
    }

    [Theory]
    [InlineData("<button onclick=\"x()\">")]
    [InlineData("<a href=\"#\" ONFOCUS = \"x()\">")]
    [InlineData("<div id=\"a\" onmouseover='x()'>")]
    public void InlineEventHandlerPattern_RealHandlerAttributes_AreDetected(string markup)
    {
        Assert.Matches(InlineEventHandlerPattern(), markup);
    }

    [Theory]
    [InlineData("<button blazor:onclick=\"1\">")]
    [InlineData("<p>onclick=\"x\"</p>")]
    [InlineData("<div id=\"conditions\">")]
    public void InlineEventHandlerPattern_BlazorMarkersAndText_AreNotDetected(string markup)
    {
        Assert.DoesNotMatch(InlineEventHandlerPattern(), markup);
    }
}
