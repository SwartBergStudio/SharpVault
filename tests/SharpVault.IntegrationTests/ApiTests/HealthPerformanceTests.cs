// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Net;
using SharpVault.IntegrationTests.Factories;
using SharpVault.ServiceDefaults;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Performance")]
public sealed class HealthPerformanceTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const int MaxColdBurstStorageCalls = 3;

    private static readonly TimeSpan FirstUncachedBudget = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan CachedBudget = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan AliveBudget = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan UnknownRouteBudget = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task HealthEndpoint_FirstUncachedRequest_CompletesUnder1000Milliseconds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAsync(cancellationToken);
        using var isolated = Factory.WithWebHostBuilder(_ => { });
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        var elapsed = await MeasureAsync(client, HealthEndpoints.HealthPath, cancellationToken);

        Assert.True(elapsed < FirstUncachedBudget, $"Uncached /health took {elapsed.TotalMilliseconds:F0} ms");
        var recorded = Assert.Single(StorageRequests.Requests);
        Assert.False(recorded.IsEntityQuery);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task HealthEndpoint_FiftyCachedRequests_EachCompletesUnder100Milliseconds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAsync(cancellationToken);
        using var isolated = Factory.WithWebHostBuilder(_ => { });
        using var client = isolated.CreateClient();
        await client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);
        StorageRequests.Clear();

        for (var i = 0; i < 50; i++)
        {
            var elapsed = await MeasureAsync(client, $"{HealthEndpoints.HealthPath}?x={i}", cancellationToken);
            Assert.True(elapsed < CachedBudget, $"Cached /health request {i} took {elapsed.TotalMilliseconds:F0} ms");
        }

        Assert.Equal(0, StorageRequests.Count);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task AliveEndpoint_FiftyRequests_EachCompletesUnder50Milliseconds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.GetAsync(HealthEndpoints.AlivePath, cancellationToken);
        StorageRequests.Clear();

        for (var i = 0; i < 50; i++)
        {
            var elapsed = await MeasureAsync(Client, HealthEndpoints.AlivePath, cancellationToken);
            Assert.True(elapsed < AliveBudget, $"/alive request {i} took {elapsed.TotalMilliseconds:F0} ms");
        }

        Assert.Equal(0, StorageRequests.Count);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task UnknownRoute_FiftyAnonymousRequests_EachCompletesUnder100MillisecondsWithoutStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Client.GetAsync("/does-not-exist", cancellationToken);
        StorageRequests.Clear();

        for (var i = 0; i < 50; i++)
        {
            var elapsed = await MeasureAsync(Client, $"/does-not-exist/{i}", cancellationToken, HttpStatusCode.Unauthorized);
            Assert.True(elapsed < UnknownRouteBudget, $"Unknown route request {i} took {elapsed.TotalMilliseconds:F0} ms");
        }

        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public async Task HealthEndpoint_ConcurrentRequestsOnColdCache_MakesAtMostThreeStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAsync(cancellationToken);
        using var isolated = Factory.WithWebHostBuilder(_ => { });
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.GetAsync(HealthEndpoints.HealthPath, cancellationToken)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.True(StorageRequests.Count <= MaxColdBurstStorageCalls, $"Cold concurrent burst made {StorageRequests.Count} storage calls");
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task Startup_DefaultTableSet_CompletesUnder5SecondsWithNoStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAsync(cancellationToken);
        StorageRequests.Clear();
        var started = Stopwatch.GetTimestamp();

        using var derived = Factory.WithWebHostBuilder(_ => { });
        using var client = derived.CreateClient();
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"Host start took {elapsed.TotalMilliseconds:F0} ms");
        Assert.Equal(0, StorageRequests.Count);
    }

    private static async Task<TimeSpan> MeasureAsync(
        HttpClient client,
        string path,
        CancellationToken cancellationToken,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await client.GetAsync(path, cancellationToken);
        var elapsed = Stopwatch.GetElapsedTime(started);
        Assert.Equal(expected, response.StatusCode);
        return elapsed;
    }

    private async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        await Client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);
        await Client.GetAsync(HealthEndpoints.AlivePath, cancellationToken);
    }
}
