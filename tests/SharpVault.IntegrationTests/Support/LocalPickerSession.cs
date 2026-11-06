// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.RegularExpressions;
using SharpVault.Authentication.Local;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.Support;

public static partial class LocalPickerSession
{
    private const string PickerPath = "/account/local-sign-in";
    private const string SessionCookie = "__Host-SharpVault";
    private const string TokenField = "__RequestVerificationToken";
    private const string AntiforgeryCookiePrefix = ".AspNetCore.Antiforgery";

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*\\bvalue=\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    public static async Task<string> MintSessionCookieAsync(LocalWebTestFactory web, LocalAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(account);

        using var client = web.CreateWebClient();
        var form = await client.GetAsync(PickerPath, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        var html = await form.Content.ReadAsStringAsync(cancellationToken);
        var token = WebUtility.HtmlDecode(TokenPattern().Match(html).Groups["token"].Value);
        var antiforgery = Assert.Single(SetCookies(form), value => value.StartsWith(AntiforgeryCookiePrefix, StringComparison.Ordinal)).Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [TokenField] = token,
                ["accountId"] = account.Id.ToString("D"),
                ["returnUrl"] = "/",
            }),
        };
        request.Headers.Add("Cookie", antiforgery);
        var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var session = Assert.Single(SetCookies(response), value => value.StartsWith(SessionCookie + "=", StringComparison.Ordinal));
        return session.Split(';')[0];
    }

    private static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];
}
