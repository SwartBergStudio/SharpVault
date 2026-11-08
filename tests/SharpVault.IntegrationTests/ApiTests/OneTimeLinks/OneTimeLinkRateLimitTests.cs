// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Authentication.WebGateway;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkRateLimitTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private const string OpenLimitSetting = "VaultRateLimits:OneTimeLinkOpensPerMinute";
    private const string RevealLimitSetting = "VaultRateLimits:RevealsPerMinute";
    private const string SharingLimitSetting = "VaultRateLimits:ShareWritesPerMinute";
    private const string TwoPerMinute = "2";
    private const string OneAddressA = "198.51.100.10";
    private const string OneAddressB = "198.51.100.11";

    [Fact]
    public async Task Open_OverTheLimit_Returns429WithRetryAfter()
    {
        using var host = LimitedHost(OpenLimitSetting, TwoPerMinute);
        var missing = Guid.NewGuid();

        using (var first = await OpenFromAsync(host, missing, OneAddressA))
        using (var second = await OpenFromAsync(host, missing, OneAddressA))
        {
            Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        }

        using var limited = await OpenFromAsync(host, missing, OneAddressA);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Open_OverTheLimit_Returns429BeforeAnyStorageCall()
    {
        using var host = LimitedHost(OpenLimitSetting, TwoPerMinute);
        var missing = Guid.NewGuid();
        using (await OpenFromAsync(host, missing, OneAddressA))
        using (await OpenFromAsync(host, missing, OneAddressA))
        {
        }

        Assert.NotEmpty(StorageRequests.Requests);
        StorageRequests.Clear();
        using var limited = await OpenFromAsync(host, missing, OneAddressA);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Empty(StorageRequests.Requests);
    }

    [Fact]
    public async Task Open_OneAddressLimited_OtherAddressStillWorks()
    {
        using var host = LimitedHost(OpenLimitSetting, TwoPerMinute);
        var missing = Guid.NewGuid();
        using (await OpenFromAsync(host, missing, OneAddressA))
        using (await OpenFromAsync(host, missing, OneAddressA))
        using (var limited = await OpenFromAsync(host, missing, OneAddressA))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }

        using var other = await OpenFromAsync(host, missing, OneAddressB);

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
    }

    [Fact]
    public async Task Open_Ipv6SameSlash64_SharesAPartition()
    {
        using var host = LimitedHost(OpenLimitSetting, TwoPerMinute);
        var missing = Guid.NewGuid();
        using (await OpenFromAsync(host, missing, "2001:db8:1:2::1"))
        using (await OpenFromAsync(host, missing, "2001:db8:1:2::2"))
        using (var limited = await OpenFromAsync(host, missing, "2001:db8:1:2::3"))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }

        using var otherPrefix = await OpenFromAsync(host, missing, "2001:db8:1:3::1");

        Assert.Equal(HttpStatusCode.NotFound, otherPrefix.StatusCode);
    }

    [Fact]
    public async Task Open_MissingAddress_UsesTheUnknownPartition()
    {
        using var host = LimitedHost(OpenLimitSetting, TwoPerMinute);
        var missing = Guid.NewGuid();
        using (await OpenFromAsync(host, missing, string.Empty))
        using (await OpenFromAsync(host, missing, string.Empty))
        using (var limited = await OpenFromAsync(host, missing, string.Empty))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        }

        using var named = await OpenFromAsync(host, missing, OneAddressA);

        Assert.Equal(HttpStatusCode.NotFound, named.StatusCode);
    }

    [Fact]
    public async Task Snapshot_UsesTheRevealLimit()
    {
        var secret = await NewSecretAsync();
        using var host = LimitedHost(RevealLimitSetting, "1");
        using var client = CreateClientFor(host, TestTokens.Create(Owner));

        using (var first = await client.PostAsync(SecretRoute(ApiRoutes.OneTimeLinkSecretSnapshot, OwnerId, secret.Id), null, Token))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var second = await client.PostAsync(SecretRoute(ApiRoutes.OneTimeLinkSecretSnapshot, OwnerId, secret.Id), null, Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Create_UsesTheSharingLimit()
    {
        var secret = await NewSecretAsync();
        using var host = LimitedHost(SharingLimitSetting, "1");
        using var client = CreateClientFor(host, TestTokens.Create(Owner));
        var route = SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, secret.Id);

        using (var first = await client.PostAsJsonAsync(route, new CreateOneTimeLinkRequestDto { Payload = NewPayload(out _) }, Token))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using var second = await client.PostAsJsonAsync(route, new CreateOneTimeLinkRequestDto { Payload = NewPayload(out _) }, Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    private WebApplicationFactory<Program> LimitedHost(string setting, string value) =>
        Factory.WithWebHostBuilder(builder => builder
            .UseSetting(setting, value)
            .ConfigureServices(services => services.AddSingleton<IStartupFilter, EmptyHeaderRestoringStartupFilter>()));

    private static async Task<HttpResponseMessage> OpenFromAsync(WebApplicationFactory<Program> host, Guid linkId, string clientIp)
    {
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, clientIp, OpenAgent);
        if (clientIp.Length == 0)
        {
            request.Headers.TryAddWithoutValidation(EmptyHeaderRestoringStartupFilter.ListHeader, WebGatewayHeaders.ClientIp);
        }

        using var client = CreateClientFor(host);
        return await client.SendAsync(request, Token);
    }
}
