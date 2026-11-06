// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using SharpVault.Authentication.Local;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Performance")]
public sealed partial class LocalSignInPerformanceTests
{
    private const string PickerPath = "/account/local-sign-in";
    private const int Requests = 50;

    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(150);

    public LocalSignInPerformanceTests(LocalApiTestFactory api, LocalWebTestFactory web)
    {
        Web = web;
        web.UseApi(api);
    }

    private LocalWebTestFactory Web { get; }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*\\bvalue=\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    [Fact]
    public async Task PostPicker_FiftyRequests_EachCompletesUnder150Milliseconds()
    {
        using var client = Web.CreateWebClient();
        await SignInOnceAsync(client);

        for (var index = 0; index < Requests; index++)
        {
            var (token, cookie) = await FetchAsync(client);
            var stopwatch = Stopwatch.StartNew();
            using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
                    ["returnUrl"] = "/",
                }),
            };
            request.Headers.Add("Cookie", cookie);
            var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
            stopwatch.Stop();

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.True(stopwatch.Elapsed < Budget, $"Request {index} took {stopwatch.ElapsedMilliseconds} ms.");
        }
    }

    private static async Task SignInOnceAsync(HttpClient client)
    {
        var (token, cookie) = await FetchAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, PickerPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["accountId"] = LocalAccounts.UserOne.Id.ToString("D"),
            }),
        };
        request.Headers.Add("Cookie", cookie);
        await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<(string Token, string Cookie)> FetchAsync(HttpClient client)
    {
        var response = await client.GetAsync(PickerPath, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var token = WebUtility.HtmlDecode(TokenPattern().Match(html).Groups["token"].Value);
        var cookie = response.Headers.GetValues("Set-Cookie").First(value => value.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
        return (token, cookie.Split(';')[0]);
    }
}
