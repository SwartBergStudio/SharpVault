// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using AutoFixture;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class ErrorHandlingTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task UnhandledException_Development_ReturnsProblemDetailsWithoutDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = CreateThrowingFactory();
        using var client = throwing.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        AssertBodyHasNoInternals(body);
        SecurityHeaderAssertions.AssertApi(response);
    }

    [Fact]
    public async Task UnhandledException_Development_DoesNotRenderDeveloperExceptionPage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = CreateThrowingFactory();
        using var client = throwing.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        AssertBodyHasNoInternals(body);
        Assert.DoesNotContain("<html", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnhandledException_Development_ExceptionIsLoggedServerSideOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = CreateThrowingFactory();
        using var client = throwing.CreateClient();
        Logs.Clear();

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(SensitiveMarkers.ExceptionMessage, body, StringComparison.Ordinal);
        Assert.True(Logs.ContainsText(SensitiveMarkers.ExceptionMessage));
    }

    [Fact]
    public async Task UnhandledException_Development_ResponseHeadersDoNotLeakExceptionDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var throwing = CreateThrowingFactory();
        using var client = throwing.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", cancellationToken);

        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            var value = string.Join(',', header.Value);
            Assert.DoesNotContain(SensitiveMarkers.ExceptionMessage, value, StringComparison.Ordinal);
            Assert.DoesNotContain("InvalidOperationException", value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UnknownRoute_AnonymousCaller_ReturnsProblemDetailsBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        AssertBodyHasNoInternals(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task UnknownRoute_AuthenticatedCaller_ReturnsProblemDetailsBodyWithoutRequestedPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);
        var path = "/secret-" + Fixture.Create<Guid>().ToString("N");

        var response = await Client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(path, body, StringComparison.Ordinal);
        AssertBodyHasNoInternals(body);
    }

    [Fact]
    public async Task UnknownRoute_AnonymousCaller_ErrorResponseHasSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        SecurityHeaderAssertions.AssertApi(response);
    }

    [Fact]
    public async Task UnknownRoute_AuthenticatedCaller_NotFoundResponseHasSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);

        var response = await Client.GetAsync("/does-not-exist", cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        SecurityHeaderAssertions.AssertApi(response);
    }

    [Fact]
    public async Task UnknownRoute_MalformedJsonBody_Returns401WithoutParsingBody()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/v1/secrets", content, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertBodyHasNoInternals(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    [Fact]
    public async Task UnknownRoute_OversizedBodyFromAnonymousCaller_IsRejectedWithoutServerError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var content = new ByteArrayContent(new byte[2 * 1024 * 1024]);

        var response = await Client.PostAsync("/api/v1/secrets", content, cancellationToken);

        Assert.True((int)response.StatusCode is 401 or 413, $"Unexpected status {(int)response.StatusCode}");
    }

    [Theory]
    [InlineData("/does-not-exist?x=%00")]
    [InlineData("/does-not-exist/%2e%2e/%2e%2e/etc/passwd")]
    [InlineData("/%E0%A4%A")]
    [InlineData("/does-not-exist;jsessionid=1")]
    public async Task UnknownRoute_HostilePath_NeverReturnsServerError(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await Client.GetAsync(path, cancellationToken);

        Assert.True((int)response.StatusCode < 500, $"Unexpected status {(int)response.StatusCode} for {path}");
        AssertBodyHasNoInternals(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static void AssertBodyHasNoInternals(string body)
    {
        Assert.DoesNotContain(SensitiveMarkers.ExceptionMessage, body, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" at SharpVault", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountKey", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("devstoreaccount1", body, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> CreateThrowingFactory() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Configure<OpenApiOptions>("v1", options => options.AddDocumentTransformer((document, context, token) =>
                throw new InvalidOperationException(SensitiveMarkers.ExceptionMessage)))));
}
