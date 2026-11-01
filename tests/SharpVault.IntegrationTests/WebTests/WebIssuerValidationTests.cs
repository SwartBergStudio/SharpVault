// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class WebIssuerValidationTests(WebTestFactory factory)
{
    private const string ErrorPath = "/account/sign-in-error";
    private const string MetadataLogMarker = "Obtaining information from metadata endpoint";
    private const string MetadataLogCode = "IDX20805";

    public static TheoryData<string> ForeignIssuers() =>
    [
        TestIdentityProvider.Issuer(TestTenant.OtherId),
        $"https://sts.windows.net/{TestTenant.Id:D}/",
        $"https://{Guid.NewGuid():N}.example.test/{Guid.NewGuid():D}/v2.0",
    ];

    [Theory]
    [MemberData(nameof(ForeignIssuers))]
    public async Task SignInCallback_IdTokenWithForeignIssuer_IsRejectedWithoutMetadataRequest(string issuer)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var outbound = new RecordingHandler();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = outbound))));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        var challenge = await client.GetAsync("/account/sign-in", cancellationToken);
        var state = WebTestSupport.QueryValue(challenge.Headers.Location, "state")!;
        var nonce = WebTestSupport.QueryValue(challenge.Headers.Location, "nonce")!;
        var idToken = TestTokens.For(TestUsers.User)
            .WithClaim("aud", TestWebApp.ClientId.ToString("D"))
            .WithClaim("nonce", nonce)
            .WithClaim("iss", issuer)
            .WithoutClaim("scp")
            .Build();
        factory.Logs.Clear();

        using var response = await client.PostAsync(
            "/signin-oidc",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["state"] = state, ["id_token"] = idToken }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ErrorPath, response.Headers.Location?.OriginalString);
        Assert.Empty(outbound.Requests);
        Assert.False(factory.Logs.ContainsText(MetadataLogMarker), "A metadata endpoint request was logged.");
        Assert.False(factory.Logs.ContainsText(MetadataLogCode), "A metadata endpoint request was logged.");
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _requests = new();

        public IReadOnlyCollection<string> Requests => [.. _requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Enqueue(request.RequestUri?.GetLeftPart(UriPartial.Path) ?? "unknown");
            return Task.FromException<HttpResponseMessage>(new HttpRequestException("Outbound HTTP is not allowed in this test."));
        }
    }
}
