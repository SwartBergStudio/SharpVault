// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.ServiceDefaults.Security;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(ProductionTestGroup))]
[Trait("Category", "Security")]
public sealed class ProductionHostingTests(ProductionApiTestFactory factory)
{
    private const string AzuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private const string AzuriteConnectionString =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=" + AzuriteAccountKey +
        ";TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";

    public static TheoryData<CallerKind> ForgedCallers() =>
        new() { CallerKind.Anonymous, CallerKind.AlgNoneToken, CallerKind.UnknownKeyToken };

    public static TheoryData<string, CallerKind> LockedPathsAndCallers()
    {
        var data = new TheoryData<string, CallerKind>();
        foreach (var path in new[] { "/openapi/v1.json", "/scalar/v1", "/scalar", "/scalar/scalar.js", "/does-not-exist", "/api/v1/secrets", "/health/extra" })
        {
            foreach (var caller in new[] { CallerKind.Anonymous, CallerKind.AlgNoneToken, CallerKind.UnknownKeyToken })
            {
                data.Add(path, caller);
            }
        }

        return data;
    }

    [Fact]
    public void Startup_ProductionWithAccountKeyConnectionString_FailsWithoutEchoingKey()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:tables", AzuriteConnectionString));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.DoesNotContain("AccountKey", invalid.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AzuriteAccountKey, invalid.ToString(), StringComparison.Ordinal);
        Assert.False(factory.Logs.ContainsText(AzuriteAccountKey));
    }

    [Theory]
    [InlineData("UseDevelopmentStorage=true")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=prod;AccountKey=c2VjcmV0LWtleS1mb3ItdGVzdA==;EndpointSuffix=core.windows.net")]
    [InlineData("BlobEndpoint=https://x.blob.core.windows.net/;SharedAccessSignature=sv=2024-01-01&sig=abc")]
    public void Startup_ProductionWithAnyConnectionString_FailsWithoutEchoingValue(string connectionString)
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:tables", connectionString));

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
        Assert.DoesNotContain(connectionString, exception.ToString(), StringComparison.Ordinal);
        Assert.False(factory.Logs.ContainsText(connectionString));
        Assert.False(factory.Logs.ContainsText("c2VjcmV0LWtleS1mb3ItdGVzdA=="));
    }

    [Fact]
    public void Startup_ProductionWithSasServiceUri_Fails()
    {
        const string sasUri = "https://example.table.core.windows.net/?sv=2024-01-01&sig=abc";
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:tables", sasUri));

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
        Assert.DoesNotContain("sig=abc", exception.ToString(), StringComparison.Ordinal);
        Assert.False(factory.Logs.ContainsText("sig=abc"));
    }

    [Fact]
    public void Startup_ProductionWithHttpServiceUri_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:tables", "http://example.table.core.windows.net/"));

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Startup_ProductionWithUserInfoInServiceUri_FailsWithoutEchoingCredentials()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:tables", "https://user:hunter2@example.table.core.windows.net/"));

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
        Assert.DoesNotContain("hunter2", exception.ToString(), StringComparison.Ordinal);
        Assert.False(factory.Logs.ContainsText("hunter2"));
    }

    [Fact]
    public void Startup_ProductionWithNothingConfigured_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:tables", string.Empty));

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void Startup_StagingWithAccountKeyConnectionString_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("ConnectionStrings:tables", AzuriteConnectionString);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
        Assert.DoesNotContain(AzuriteAccountKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LockedPathsAndCallers))]
    public async Task Production_LockedRoute_Returns401(string path, CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, caller);

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiDocument_Production_Returns401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiReference_Production_Returns401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/scalar/v1", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Endpoints_ProductionEnvironment_OnlyHealthEndpointsAreAnonymous()
    {
        var anonymous = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Order()
            .ToList();

        Assert.Equal(["/alive", "/health"], anonymous);
    }

    [Fact]
    public void Endpoints_ProductionEnvironment_DoesNotMapDocumentationRoutes()
    {
        var patterns = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(patterns, pattern => pattern.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(patterns, pattern => pattern.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnknownRoute_ProductionAnonymous_Returns401WithBearerChallenge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("Bearer", response.HeaderValue("WWW-Authenticate"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_ProductionAlgNoneToken_Returns401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, CallerKind.AlgNoneToken);

        var response = await client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ProductionTokenSignedWithUnknownKey_Returns401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, CallerKind.UnknownKeyToken);

        var response = await client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Bearer", "")]
    [InlineData("Bearer", "garbage")]
    [InlineData("Bearer", "a.b.c")]
    [InlineData("Bearer", "..")]
    [InlineData("Basic", "YWRtaW46YWRtaW4=")]
    [InlineData("Negotiate", "abc")]
    [InlineData("bearer", "garbage")]
    public async Task UnknownRoute_ProductionMalformedAuthorizationHeader_Returns401(string scheme, string parameter)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/secrets");
        request.Headers.TryAddWithoutValidation("Authorization", $"{scheme} {parameter}".TrimEnd());

        var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ProductionTestAuthenticationHeaders_Returns401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();
        SpoofedTestHeaders.Add(client.DefaultRequestHeaders, TestUsers.Admin);

        var response = await client.GetAsync("/api/v1/secrets", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_ProductionForgedToken_TokenNotLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = ForgedTokens.SignedWithUnknownKey();
        using var client = factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await client.GetAsync("/does-not-exist", cancellationToken);

        Assert.NotEmpty(factory.Logs.Entries);
        foreach (var segment in token.Split('.'))
        {
            Assert.False(factory.Logs.ContainsText(segment));
        }
    }

    [Fact]
    public async Task UnknownRoute_ProductionAlgNoneToken_TokenNotLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = ForgedTokens.AlgNone();
        using var client = factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await client.GetAsync("/does-not-exist", cancellationToken);

        Assert.False(factory.Logs.ContainsText(token));
        Assert.False(factory.Logs.ContainsText(token.Split('.')[1]));
    }

    [Fact]
    public async Task UnknownRoute_ProductionUnauthorizedResponse_DoesNotLeakValidationDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, CallerKind.UnknownKeyToken);

        var response = await client.GetAsync("/does-not-exist", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var challenge = response.HeaderValue("WWW-Authenticate") ?? string.Empty;
        Assert.DoesNotContain("IDX", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.IdentityModel", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IDX", challenge, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.IdentityModel", challenge, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApiResponse_Production_HasStrictContentSecurityPolicy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/alive", cancellationToken);

        Assert.Equal(SecurityHeaderValues.ApiContentSecurityPolicy, response.HeaderValue(SecurityHeaderNames.ContentSecurityPolicy));
        SecurityHeaderAssertions.AssertApi(response);
    }

    [Theory]
    [InlineData("/does-not-exist")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar/v1")]
    public async Task ApiResponse_ProductionErrorResponses_HaveSecurityHeadersAndNoStore(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();

        var response = await client.GetAsync(path, cancellationToken);

        SecurityHeaderAssertions.AssertApi(response);
        Assert.Equal(SecurityHeaderValues.NoStore, response.HeaderValue(SecurityHeaderNames.CacheControl));
    }

    [Theory]
    [MemberData(nameof(ForgedCallers))]
    public async Task AliveEndpoint_Production_Returns200Anonymously(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, caller);

        var response = await client.GetAsync("/alive", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task AliveEndpoint_ProductionUnsupportedMethod_Returns401(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();

        var response = await client.SendAsync(method, "/alive", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CorsPreflight_ProductionUntrustedOrigin_ReturnsNoAllowOriginHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/alive");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request, cancellationToken);

        Assert.Null(response.HeaderValue("Access-Control-Allow-Origin"));
    }
}
