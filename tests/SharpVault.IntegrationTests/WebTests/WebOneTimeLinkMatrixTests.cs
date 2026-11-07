// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebApiTestGroup))]
[Trait("Category", "Security")]
public sealed class WebOneTimeLinkMatrixTests : IAsyncLifetime
{
    private const string LinkOpenHeader = "X-SharpVault-Link-Open";
    private const string LinkOpenHeaderValue = "1";
    private const string BrowserAgent = "SharpVault-Test-Browser/1.0";
    private const string UnavailableJson = "{\"status\":\"unavailable\"}";
    private const string RateLimitedJson = "{\"status\":\"rate-limited\"}";

    private readonly ApiTestFactory _api;
    private readonly WebTestFactory _web;

    public WebOneTimeLinkMatrixTests(ApiTestFactory api, WebTestFactory web)
    {
        _api = api;
        _web = web;
        _web.UseApi(api);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _api.ResetStorageAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    public static TheoryData<CallerKind> Callers =>
    [
        CallerKind.Anonymous,
        CallerKind.User,
        CallerKind.LockedUser,
        CallerKind.Admin,
    ];

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task WebOneTimeLinkMatrix_EveryCaller_ReturnsExpectedStatus(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var linkId = await CreateLinkAsync();
        using var visitor = await VisitorAsync(caller, cancellationToken);

        using var page = await visitor.GetAsync($"/l/{linkId:D}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        using var opened = await PostOpenAsync(visitor, linkId, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var payload = await opened.Content.ReadFromJsonAsync<OneTimeLinkPayloadDto>(cancellationToken);
        Assert.False(string.IsNullOrEmpty(payload?.Payload));

        using var again = await PostOpenAsync(visitor, linkId, cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task WebOpen_WithoutTheOpenHeader_DoesNotConsumeTheLink()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var linkId = await CreateLinkAsync();
        using var visitor = NewVisitor();
        var path = $"/l/{linkId:D}/open";

        using (var withoutHeader = await visitor.PostAsync(path, null, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, withoutHeader.StatusCode);
            Assert.Equal(UnavailableJson, await withoutHeader.Content.ReadAsStringAsync(cancellationToken));
        }

        using var opened = await PostOpenAsync(visitor, linkId, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task WebOpen_EveryUnavailableState_ReturnsIdenticalResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var used = await CreateLinkAsync();
        using (var first = await PostOpenAsync(NewVisitor(), used, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        var live = await CreateLinkAsync();
        using var visitor = NewVisitor();
        var answers = new List<(string Name, HttpResponseMessage Response)>
        {
            ("unknown", await PostOpenAsync(visitor, Guid.NewGuid(), cancellationToken)),
            ("used", await PostOpenAsync(visitor, used, cancellationToken)),
            ("malformed id", await PostRawAsync(visitor, "/l/not-a-guid/open", LinkOpenHeaderValue, cancellationToken)),
            ("missing header", await PostRawAsync(visitor, $"/l/{live:D}/open", null, cancellationToken)),
        };

        try
        {
            foreach (var (name, response) in answers)
            {
                Assert.True(response.StatusCode == HttpStatusCode.NotFound, $"The {name} answer was {(int)response.StatusCode}.");
                Assert.Equal(UnavailableJson, await response.Content.ReadAsStringAsync(cancellationToken));
                Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());
            }
        }
        finally
        {
            foreach (var (_, response) in answers)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task WebOpen_ApiReturns500_ReturnsTheGenericUnavailableResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var linkId = await CreateLinkAsync();
        using var visitor = NewVisitor();
        _web.OpenFault.Status = HttpStatusCode.InternalServerError;

        try
        {
            using var response = await PostOpenAsync(visitor, linkId, cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(UnavailableJson, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        finally
        {
            _web.OpenFault.Status = null;
        }
    }

    [Fact]
    public async Task WebOpen_ApiRateLimited_Returns429WithRetryAfter()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var linkId = await CreateLinkAsync();
        using var visitor = NewVisitor();
        _web.OpenFault.Status = HttpStatusCode.TooManyRequests;
        _web.OpenFault.RetryAfter = TimeSpan.FromSeconds(30);

        try
        {
            using var response = await PostOpenAsync(visitor, linkId, cancellationToken);

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.RetryAfter?.Delta);
            Assert.Equal(RateLimitedJson, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        finally
        {
            _web.OpenFault.Status = null;
            _web.OpenFault.RetryAfter = null;
        }
    }

    [Fact]
    public async Task WebOpen_Preflight_ReturnsNoCorsAllowance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var linkId = await CreateLinkAsync();
        using var visitor = NewVisitor();
        using var request = new HttpRequestMessage(HttpMethod.Options, $"/l/{linkId:D}/open");
        request.Headers.TryAddWithoutValidation("Origin", "https://attacker.example");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", LinkOpenHeader);

        using var response = await visitor.SendAsync(request, cancellationToken);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        using var opened = await PostOpenAsync(visitor, linkId, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task WebOpen_VisitorSendsGatewayHeaders_AreIgnored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var linkId = await CreateLinkAsync();
        using var visitor = NewVisitor();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/l/{linkId:D}/open");
        request.Headers.TryAddWithoutValidation(LinkOpenHeader, LinkOpenHeaderValue);
        request.Headers.TryAddWithoutValidation("X-WebGateway-Client-Ip", "198.51.100.9");
        request.Headers.TryAddWithoutValidation("X-WebGateway-Signature", "AAAA");
        request.Headers.TryAddWithoutValidation("X-WebGateway-Time", "0");

        using var response = await visitor.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> CreateLinkAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var owner = TestUsers.User;
        using var client = _api.CreateAuthenticatedClient(owner);
        var vault = new VaultTestApi(client);
        var (secretResponse, secret) = await vault.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = "SV-NAME-" + Guid.NewGuid().ToString("N"),
            Value = "SV-VALUE-" + Guid.NewGuid().ToString("N"),
        });
        Assert.True(secretResponse.IsSuccessStatusCode, $"Secret setup failed with {secretResponse.StatusCode}.");
        var secretId = secret!.Id;

        var route = ApiRoutes.OneTimeLinkSecret
            .Replace("{ownerId:guid}", owner.ObjectId.ToString("D"), StringComparison.Ordinal)
            .Replace("{secretId:guid}", secretId.ToString("D"), StringComparison.Ordinal);
        using var created = await client.PostAsJsonAsync(route, new CreateOneTimeLinkRequestDto { Payload = NewPayload() }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<OneTimeLinkDto>(cancellationToken);
        return body!.Id;
    }

    private async Task<HttpClient> VisitorAsync(CallerKind caller, CancellationToken cancellationToken) => caller switch
    {
        CallerKind.Anonymous => NewVisitor(),
        CallerKind.LockedUser => WithBrowserAgent(await SignedInAsLockedAsync(cancellationToken)),
        _ => WithBrowserAgent(await _web.CreateSignedInClientAsync(CallerClients.UserFor(caller)!, cancellationToken)),
    };

    private static HttpClient WithBrowserAgent(HttpClient visitor)
    {
        visitor.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserAgent);
        return visitor;
    }

    private async Task<HttpClient> SignedInAsLockedAsync(CancellationToken cancellationToken)
    {
        await _api.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        return await _web.CreateSignedInClientAsync(TestUsers.LockedUser, cancellationToken);
    }

    private static async Task<HttpResponseMessage> PostOpenAsync(HttpClient visitor, Guid linkId, CancellationToken cancellationToken) =>
        await PostRawAsync(visitor, $"/l/{linkId:D}/open", LinkOpenHeaderValue, cancellationToken);

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient visitor, string path, string? header, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(LinkOpenHeader, header);
        }

        return await visitor.SendAsync(request, cancellationToken);
    }

    private HttpClient NewVisitor()
    {
        var visitor = _web.CreateWebClient();
        WithBrowserAgent(visitor);
        return visitor;
    }

    private static string NewPayload()
    {
        var marker = "PLAINTEXT-" + Guid.NewGuid().ToString("N");
        var bytes = new byte[1 + marker.Length];
        bytes[0] = 0x80;
        Encoding.ASCII.GetBytes(marker, bytes.AsSpan(1));
        return Convert.ToBase64String(bytes);
    }
}
