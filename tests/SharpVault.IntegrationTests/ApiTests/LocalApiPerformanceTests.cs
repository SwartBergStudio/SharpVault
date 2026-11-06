// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Authentication.Local;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services.Auth;
using WebApp::SharpVault.Web.Api;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Performance")]
public sealed class LocalApiPerformanceTests(LocalApiTestFactory api, LocalWebTestFactory web) : IAsyncLifetime
{
    private const string MePath = "/api/me";
    private const string CacheTtlSetting = "UserAccess:CacheTtl";
    private const string ThirtySecondTtl = "00:00:30";
    private const int WarmUpRequests = 5;
    private const int MeasuredRequests = 50;
    private const int MintCalls = 100;
    private const double P95Percentile = 0.95;

    private static readonly TimeSpan CacheHitBudget = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MintBudget = TimeSpan.FromMilliseconds(5);

    public async ValueTask InitializeAsync()
    {
        web.UseApi(api);
        await api.ResetAndSeedAsync(CancellationToken.None);
        api.StorageRequests.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Assert.Empty(api.StorageRequests.QueriesWithoutPartitionKey());
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task LocalMode_GetMe_CachedAccess_FiftyRequestsEachUnder50MillisecondsWithoutStorageCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var cached = api.WithWebHostBuilder(builder => builder.UseSetting(CacheTtlSetting, ThirtySecondTtl));
        using var client = LocalApiTestFactory.CreateClientFor(cached, LocalTokens.For(cached.Services, LocalAccounts.UserOne));
        for (var warmUp = 0; warmUp < WarmUpRequests; warmUp++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MePath, cancellationToken)).StatusCode);
        }

        api.StorageRequests.Clear();
        var timings = new List<TimeSpan>();
        for (var index = 0; index < MeasuredRequests; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await client.GetAsync(MePath, cancellationToken);
            stopwatch.Stop();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            timings.Add(stopwatch.Elapsed);
        }

        var slowest = timings.Max();
        Assert.True(slowest < CacheHitBudget, $"Slowest of {MeasuredRequests} requests took {slowest.TotalMilliseconds:F0} ms.");
        Assert.Equal(0, api.StorageRequests.Count);
    }

    [Fact]
    public async Task LocalMode_TokenMintPerApiCall_AddsUnderFiveMillisecondsAtP95()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var scope = web.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IApiAccessTokenProvider>();
        Assert.IsType<LocalApiAccessTokenProvider>(provider);
        var principal = LocalPrincipalFactory.Create(LocalAccounts.UserOne, "PerformanceTest");
        var baseline = new NoOpTokenProvider();
        await MeasureAsync(provider, principal, WarmUpRequests, cancellationToken);

        var minted = await MeasureAsync(provider, principal, MintCalls, cancellationToken);
        var none = await MeasureAsync(baseline, principal, MintCalls, cancellationToken);

        var added = PercentileMilliseconds(minted, P95Percentile) - PercentileMilliseconds(none, P95Percentile);
        Assert.True(added < MintBudget.TotalMilliseconds, $"Minting added {added:F2} ms at p95.");
    }

    private static async Task<List<TimeSpan>> MeasureAsync(
        IApiAccessTokenProvider provider,
        ClaimsPrincipal principal,
        int calls,
        CancellationToken cancellationToken)
    {
        var timings = new List<TimeSpan>(calls);
        for (var index = 0; index < calls; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            await provider.GetAccessTokenAsync(principal, cancellationToken);
            stopwatch.Stop();
            timings.Add(stopwatch.Elapsed);
        }

        return timings;
    }

    private static double PercentileMilliseconds(List<TimeSpan> timings, double percentile)
    {
        var ordered = timings.Order().ToList();
        var position = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        return ordered[position].TotalMilliseconds;
    }

    private sealed class NoOpTokenProvider : IApiAccessTokenProvider
    {
        public bool InteractiveSignInAvailable => false;

        public Task<ApiAccessToken> GetAccessTokenAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) =>
            Task.FromResult(ApiAccessToken.NoToken);
    }
}
