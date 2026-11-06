// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using SharpVault.Dtos.Vault;
using SharpVault.ServiceDefaults;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class VaultSecretsApiClientResilienceTests : IDisposable
{
    private const string ClientName = "hanging-api";
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(100);

    private readonly ApiClientHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task RealClient_ApiHangs_ReturnsFailedInsteadOfThrowing()
    {
        using var http = BuildHangingClient();
        var client = new VaultSecretsApiClient(http, _harness.Tokens, _harness.Authentication, _harness.Session, NullLogger<VaultSecretsApiClient>.Instance);

        var created = await client.CreateSecretAsync(new CreateSecretRequestDto { Name = "Mail", Type = SecretTypeDto.Login }, TestContext.Current.CancellationToken);
        var read = await client.GetSecretAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, created.Status);
        Assert.Equal(ApiCallStatus.Failed, read.Status);
    }

    [Fact]
    public async Task RealClient_CallerCancels_StillThrowsCancellation()
    {
        using var http = BuildHangingClient();
        var client = new VaultSecretsApiClient(http, _harness.Tokens, _harness.Authentication, _harness.Session, NullLogger<VaultSecretsApiClient>.Instance);
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetSecretAsync(Guid.NewGuid(), source.Token));
    }

    private static HttpClient BuildHangingClient()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient(ClientName, client => client.BaseAddress = new Uri("https://api.example.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => new HangingHandler());
        builder.Services.Configure<HttpStandardResilienceOptions>(
            ClientName + "-standard",
            options =>
            {
                options.AttemptTimeout.Timeout = AttemptTimeout;
                options.CircuitBreaker.SamplingDuration = AttemptTimeout * 2;
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.UseJitter = false;
            });
        var app = builder.Build();
        return app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }
}
