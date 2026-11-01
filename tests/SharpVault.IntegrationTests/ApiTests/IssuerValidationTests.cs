// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class IssuerValidationTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const string MetadataLogMarker = "Obtaining information from metadata endpoint";
    private const string MetadataLogCode = "IDX20805";
    private const string MePath = "/api/me";

    public static TheoryData<string> ForeignIssuers() =>
    [
        TestIdentityProvider.Issuer(TestTenant.OtherId),
        $"https://sts.windows.net/{TestTenant.Id:D}/",
        $"https://sts.windows.net/{TestTenant.OtherId:D}/",
        $"https://{Guid.NewGuid():N}.example.test/{Guid.NewGuid():D}/v2.0",
    ];

    [Theory]
    [MemberData(nameof(ForeignIssuers))]
    public async Task OtherTenantIssuerToken_Rejected_MakesNoMetadataRequest(string issuer)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var outbound = new RecordingHandler();
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.ConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = outbound));
            services.PostConfigureAll<JwtBearerOptions>(options => options.Backchannel = new HttpClient(outbound));
        }));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        var token = TestTokens.For(TestUsers.User).WithClaim("iss", issuer).Build();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Logs.Clear();
        StorageRequests.Clear();

        using var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(outbound.Requests);
        Assert.False(Logs.ContainsText(MetadataLogMarker), "A metadata endpoint request was logged.");
        Assert.False(Logs.ContainsText(MetadataLogCode), "A metadata endpoint request was logged.");
        Assert.Equal(0, StorageRequests.Count);
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
