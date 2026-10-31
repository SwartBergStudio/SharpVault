// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Net;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebPerformanceTestGroup))]
[Trait("Category", "Performance")]
public sealed class ThemePerformanceTests(WebTestFactory factory)
{
    private const int WarmUpRequests = 5;
    private const int MeasuredRequests = 50;

    private static readonly TimeSpan AssetBudget = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan PageBudget = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task ThemeStylesheet_FiftyRequests_EachCompletesUnder50Milliseconds()
    {
        await AssertEachRequestWithinBudgetAsync(ThemeAssetPaths.Stylesheet, AssetBudget, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThemeFont_FiftyRequests_EachCompletesUnder50Milliseconds()
    {
        await AssertEachRequestWithinBudgetAsync(ThemeAssetPaths.FirstViewFont, AssetBudget, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThemeFavicon_FiftyRequests_EachCompletesUnder50Milliseconds()
    {
        await AssertEachRequestWithinBudgetAsync(ThemeAssetPaths.Favicon, AssetBudget, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HomePage_FiftyAnonymousRequests_EachCompletesUnder150Milliseconds()
    {
        await AssertEachRequestWithinBudgetAsync("/", PageBudget, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SignedOutPage_FiftyAnonymousRequests_EachCompletesUnder150Milliseconds()
    {
        await AssertEachRequestWithinBudgetAsync("/account/signed-out", PageBudget, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThemeStylesheet_RepeatedRequests_ReturnIdenticalBodies()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateWebClient();
        var first = await client.GetByteArrayAsync(ThemeAssetPaths.Stylesheet, cancellationToken);

        for (var index = 0; index < 10; index++)
        {
            var next = await client.GetByteArrayAsync(ThemeAssetPaths.Stylesheet, cancellationToken);
            Assert.Equal(first, next);
        }
    }

    private async Task AssertEachRequestWithinBudgetAsync(string path, TimeSpan budget, CancellationToken cancellationToken)
    {
        using var client = factory.CreateWebClient();
        for (var warmUp = 0; warmUp < WarmUpRequests; warmUp++)
        {
            using var warmUpResponse = await client.GetAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, warmUpResponse.StatusCode);
            await warmUpResponse.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        var timings = new List<TimeSpan>(MeasuredRequests);
        for (var measured = 0; measured < MeasuredRequests; measured++)
        {
            var started = Stopwatch.GetTimestamp();
            using var response = await client.GetAsync(path, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            timings.Add(Stopwatch.GetElapsedTime(started));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEmpty(body);
        }

        var slowest = timings.Max();
        Assert.True(
            slowest < budget,
            $"{path} slowest request took {slowest.TotalMilliseconds:F1} ms, budget {budget.TotalMilliseconds:F0} ms. Timings (ms): {string.Join(", ", timings.Select(timing => timing.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)))}");
    }
}
