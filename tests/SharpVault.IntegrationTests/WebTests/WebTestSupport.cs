// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.WebTests;

internal static partial class WebTestSupport
{
    public const string AntiforgeryFieldName = "__RequestVerificationToken";

    [GeneratedRegex("<form[^>]*\\bid=\"sign-out-form\"[^>]*>(?<body>.*?)</form>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex SignOutFormPattern();

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*\\bvalue=\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiforgeryTokenPattern();

    [GeneratedRegex("<script(?![^>]*\\bsrc=)[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScriptPattern();

    [GeneratedRegex("<[^>]+\\son[a-z]+\\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineEventHandlerPattern();

    [GeneratedRegex("<[^>]+\\sstyle\\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineStylePattern();

    [GeneratedRegex("(?:href|src)=\"(?<url>(?!https?:|//)[^\"]+\\.(?:css|js)(?:\\?[^\"]*)?)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalAssetPattern();

    public static string? ExtractSignOutAntiforgeryToken(string html)
    {
        var form = SignOutFormPattern().Match(html);
        if (!form.Success)
        {
            return null;
        }

        var token = AntiforgeryTokenPattern().Match(form.Groups["body"].Value);
        return token.Success ? WebUtility.HtmlDecode(token.Groups["token"].Value) : null;
    }

    public static bool HasInlineScriptHandlerOrStyle(string html) =>
        InlineScriptPattern().IsMatch(html) || InlineEventHandlerPattern().IsMatch(html) || InlineStylePattern().IsMatch(html);

    public static IReadOnlyList<string> LocalAssetUrls(string html) =>
        [.. LocalAssetPattern().Matches(html).Select(match => WebUtility.HtmlDecode(match.Groups["url"].Value)).Distinct(StringComparer.Ordinal)];

    public static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];

    public static string? QueryValue(Uri? location, string name) =>
        location is null ? null : HttpUtility.ParseQueryString(location.Query)[name];

    public static string SignInUrl(string returnUrl) => $"/account/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

    public static string? UnprotectedRedirectUri(WebTestFactory factory, Uri? authorizeLocation)
    {
        var state = QueryValue(authorizeLocation, "state");
        Assert.False(string.IsNullOrEmpty(state));
        var options = factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(OpenIdConnectDefaults.AuthenticationScheme);
        var properties = options.StateDataFormat.Unprotect(state);
        Assert.NotNull(properties);
        return properties.RedirectUri;
    }

    public static void AssertTenantAuthorizeRedirect(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        Assert.Equal("https", location.Scheme);
        Assert.Equal("login.microsoftonline.com", location.Host);
        Assert.Equal($"/{SharpVault.IntegrationTests.Authentication.TestTenant.Id:D}/oauth2/v2.0/authorize", location.AbsolutePath);
    }
}
