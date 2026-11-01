// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.ServiceDefaults;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
public sealed class HealthEndpointTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const string UnhealthyBody = "Unhealthy";
    private const string HealthyBody = "Healthy";

    [Fact]
    public async Task HealthEndpoint_AnonymousCallerWithHealthyStorage_Returns200WithStatusOnlyBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();

        var response = await client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HealthyBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task HealthEndpoint_AnyCall_SendsNoStoreCacheControl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();

        var uncached = await client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);
        var cached = await client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);

        Assert.Equal("no-store, no-cache", uncached.HeaderValue(SecurityHeaderNames.CacheControl));
        Assert.Equal("no-store, no-cache", cached.HeaderValue(SecurityHeaderNames.CacheControl));
    }

    [Fact]
    public async Task HealthEndpoint_StorageCall_IsServicePropertiesRequestNotEntityQuery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        var response = await client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recorded = Assert.Single(StorageRequests.Requests);
        Assert.False(recorded.IsEntityQuery);
        Assert.Equal("GET", recorded.Method);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_TwentyRequestsWithinCacheWindow_MakesOneStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        for (var i = 0; i < 20; i++)
        {
            var response = await client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, StorageRequests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_RandomQueryStrings_MakesOneStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        for (var i = 0; i < 20; i++)
        {
            var response = await client.GetAsync($"{HealthEndpoints.HealthPath}?x={Guid.NewGuid()}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, StorageRequests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_RandomHeaders_MakesOneStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        for (var i = 0; i < 20; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, HealthEndpoints.HealthPath);
            request.Headers.Add("X-Cache-Buster", Guid.NewGuid().ToString());
            request.Headers.Add("Accept-Language", Guid.NewGuid().ToString("N")[..8]);
            request.Headers.Add("Cookie", $"a={Guid.NewGuid()}");
            var response = await client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, StorageRequests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_RandomHostHeaders_MakesOneStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        for (var i = 0; i < 20; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, HealthEndpoints.HealthPath);
            request.Headers.Host = $"h{Guid.NewGuid():N}.example";
            var response = await client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, StorageRequests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_RandomHostHeadersAndQueryStrings_MakesOneStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        for (var i = 0; i < 20; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{HealthEndpoints.HealthPath}?x={Guid.NewGuid()}");
            request.Headers.Host = $"h{Guid.NewGuid():N}.example";
            var response = await client.SendAsync(request, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, StorageRequests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_HeadRequests_MakeOneStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.SendAsync("HEAD", HealthEndpoints.HealthPath, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, StorageRequests.Count);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task HealthEndpoint_UnsupportedMethodWhenAnonymous_Returns401WithoutStorageCall(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        var response = await client.SendAsync(method, HealthEndpoints.HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_PostRequest_Returns401WithoutStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        StorageRequests.Clear();

        var response = await client.PostAsync(HealthEndpoints.HealthPath, new StringContent("{}"), cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("TRACE")]
    public async Task HealthEndpoint_UnsupportedMethodNoRoleCaller_Returns403WithoutStorageCall(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.AuthenticatedWithoutRole));
        StorageRequests.Clear();

        var response = await client.SendAsync(method, HealthEndpoints.HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("TRACE")]
    public async Task HealthEndpoint_UnsupportedMethodUserCaller_Returns405WithoutHealthStorageCall(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var isolated = CreateIsolatedFactory();
        using var client = isolated.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.User));
        StorageRequests.Clear();

        var response = await client.SendAsync(method, HealthEndpoints.HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        StorageAssertions.AssertOnlyUserAndAuditTablesTouched(StorageRequests);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_CheckThrowsWithSensitiveMessage_Returns503WithStatusOnlyBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck<ThrowingHealthCheck>("throwing")));
        using var client = throwing.CreateClient();

        var response = await client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(UnhealthyBody, body);
        Assert.DoesNotContain(SensitiveMarkers.HealthCheckMessage, body, StringComparison.Ordinal);
        AssertNoSensitiveHeaders(response);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Startup_StorageUnreachable_FailsWithoutConnectionDetails()
    {
        const string accountKey = "c2VjcmV0LWtleS1mb3ItdGVzdA==";
        using var broken = Factory.WithWebHostBuilder(builder => builder.UseSetting(
            "ConnectionStrings:tables",
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={accountKey};TableEndpoint=http://127.0.0.1:1/devstoreaccount1;"));

        var exception = Record.Exception(() => broken.CreateClient());

        Assert.NotNull(exception);
        AssertLogsDoNotContain(accountKey);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task HealthEndpoint_StorageUnreachable_Returns503WithoutConnectionDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var broken = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:tables",
                "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=c2VjcmV0LWtleS1mb3ItdGVzdA==;TableEndpoint=http://127.0.0.1:1/devstoreaccount1;");
            builder.ConfigureTestServices(services => services.AddSingleton(new TableStorageTableSet([])));
        });
        using var client = broken.CreateClient();

        var response = await client.GetAsync(HealthEndpoints.HealthPath, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(UnhealthyBody, body);
        AssertLogsDoNotContain("c2VjcmV0LWtleS1mb3ItdGVzdA==");
    }

    [Fact]
    public async Task AliveEndpoint_AnonymousCaller_Returns200WithoutStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        StorageRequests.Clear();

        var response = await Client.GetAsync(HealthEndpoints.AlivePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HealthyBody, await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AliveEndpoint_StorageCheckFailing_StillReturns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHealthChecks().AddCheck<ThrowingHealthCheck>("throwing")));
        using var client = throwing.CreateClient();

        var response = await client.GetAsync(HealthEndpoints.AlivePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HealthyBody, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AliveEndpoint_ManyRequestsWithRandomQueryStrings_MakesNoStorageCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        StorageRequests.Clear();

        for (var i = 0; i < 20; i++)
        {
            var response = await Client.GetAsync($"{HealthEndpoints.AlivePath}?x={Guid.NewGuid()}", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public async Task AliveEndpoint_AnyCall_SendsNoStoreCacheControl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync(HealthEndpoints.AlivePath, cancellationToken);

        Assert.Equal("no-store, no-cache", response.HeaderValue(SecurityHeaderNames.CacheControl));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task AliveEndpoint_UnsupportedMethodWhenAnonymous_Returns401(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.SendAsync(method, HealthEndpoints.AlivePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AliveEndpoint_HeadRequest_Returns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.SendAsync("HEAD", HealthEndpoints.AlivePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void AssertNoSensitiveHeaders(HttpResponseMessage response)
    {
        foreach (var header in response.Headers)
        {
            Assert.DoesNotContain(SensitiveMarkers.HealthCheckMessage, string.Join(',', header.Value), StringComparison.Ordinal);
        }
    }

    private WebApplicationFactory<Program> CreateIsolatedFactory() => Factory.WithWebHostBuilder(_ => { });
}
