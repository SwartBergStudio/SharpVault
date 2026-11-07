// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.ServiceDefaults;

namespace SharpVault.UnitTests.ServiceDefaults;

public sealed class ServiceDefaultsExtensionsTests
{
    [Fact]
    [Trait("Category", "Security")]
    public void AddServiceDefaults_Registered_HidesServerHeaderAndLimitsRequestBodyToOneMebibyte()
    {
        using var app = BuildApp();

        var options = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.False(options.AddServerHeader);
        Assert.Equal(1_048_576, options.Limits.MaxRequestBodySize);
    }

    [Fact]
    public void AddServiceDefaults_Registered_AddsSelfCheckTaggedLive()
    {
        using var app = BuildApp();

        var registrations = app.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        var self = Assert.Single(registrations);
        Assert.Equal("self", self.Name);
        Assert.Contains("live", self.Tags);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MapDefaultEndpoints_Mapped_ExposesExactlyHealthAndAlive()
    {
        using var app = BuildApp();
        app.MapDefaultEndpoints();

        var patterns = RoutePatterns(app);

        Assert.Equal(["/alive", "/health"], patterns.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MapDefaultEndpoints_Mapped_EndpointsAreAnonymousAndAcceptOnlyGetAndHead()
    {
        using var app = BuildApp();
        app.MapDefaultEndpoints();

        foreach (var pattern in new[] { "/health", "/alive" })
        {
            var endpoint = FindEndpoint(app, pattern);

            Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(["GET", "HEAD"], methods);
        }
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("System.Net.Http.HttpClient")]
    public void SuppressQueryStringLogging_Registered_FiltersHostingDiagnosticsAndHttpClientBelowWarning(string category)
    {
        using var app = BuildApp();

        var rules = app.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules;

        Assert.Contains(rules, rule =>
            rule.ProviderName is null
            && rule.CategoryName == category
            && rule.LogLevel == LogLevel.Warning);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SuppressQueryStringLogging_AzureCore_EffectiveLevelIsWarning()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Debug" });
        builder.AddServiceDefaults();
        using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Azure.Core.Pipeline");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public void SuppressQueryStringLogging_AzureCore_DoesNotSilenceOtherCategories()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Debug" });
        builder.AddServiceDefaults();
        using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SharpVault.Api.Probe");

        Assert.True(logger.IsEnabled(LogLevel.Debug));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("System.Net.Http.HttpClient")]
    public void SuppressQueryStringLogging_ProviderSpecificConfigLowersLevel_EffectiveLevelStaysWarning(string category)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:Console:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
            ["Logging:Console:LogLevel:System.Net.Http.HttpClient"] = "Debug",
            ["Logging:OpenTelemetry:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = "Information",
            ["Logging:OpenTelemetry:LogLevel:System.Net.Http.HttpClient"] = "Debug",
        });
        builder.AddServiceDefaults();
        using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SuppressQueryStringLogging_ProviderRulesDifferOnlyByCase_EffectiveLevelStaysWarning()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.AddConsole();
        builder.Services.Configure<LoggerFilterOptions>(options =>
        {
            options.Rules.Add(new LoggerFilterRule("console", "Microsoft", LogLevel.Debug, null));
            options.Rules.Add(new LoggerFilterRule("Console", "Microsoft", LogLevel.Debug, null));
        });
        builder.AddServiceDefaults();
        using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
        var rules = app.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules;

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
        Assert.Contains(rules, rule =>
            rule.ProviderName == "console"
            && rule.CategoryName == "Microsoft.AspNetCore.Hosting.Diagnostics"
            && rule.LogLevel == LogLevel.Warning);
        Assert.Contains(rules, rule =>
            rule.ProviderName == "Console"
            && rule.CategoryName == "Microsoft.AspNetCore.Hosting.Diagnostics"
            && rule.LogLevel == LogLevel.Warning);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore.*", "Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Logging:Console:LogLevel:microsoft.aspnetcore", "Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Logging:LogLevel:System.Net.Http.HttpClient.Default", "System.Net.Http.HttpClient.Default.LogicalHandler")]
    public void SuppressQueryStringLogging_WildcardCaseOrDescendantConfig_EffectiveLevelStaysWarning(string settingKey, string category)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [settingKey] = "Debug" });
        builder.AddServiceDefaults();
        using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore.Hosting.Lifetime", "Microsoft.AspNetCore.Hosting.Lifetime")]
    [InlineData("Logging:LogLevel:SharpVault", "SharpVault.Api")]
    public void SuppressQueryStringLogging_UnrelatedCategoryConfigured_KeepsConfiguredLevel(string settingKey, string category)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [settingKey] = "Debug" });
        builder.AddServiceDefaults();
        using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.True(logger.IsEnabled(LogLevel.Debug));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Logging:Console:LogLevel:Default", "Azure.Core.Pipeline")]
    [InlineData("Logging:OpenTelemetry:LogLevel:Azure.Core", "Azure.Core.Pipeline")]
    public void SuppressQueryStringLogging_AzureCoreWithProviderSpecificConfig_EffectiveLevelStaysWarning(string settingKey, string category)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [settingKey] = "Debug" });
        builder.AddServiceDefaults();
        using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task StandardResilience_PostThatGets503_IsAttemptedOnce()
    {
        var handler = new CountingHandler(HttpStatusCode.ServiceUnavailable);
        using var client = BuildResilientClient(handler);

        using var response = await client.PostAsync("http://probe.test/write", new StringContent("{}"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task StandardResilience_GetThatGets503_IsStillRetried()
    {
        var handler = new CountingHandler(HttpStatusCode.ServiceUnavailable);
        using var client = BuildResilientClient(handler);

        using var response = await client.GetAsync("http://probe.test/read", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(handler.Attempts > 1);
    }

    private static HttpClient BuildResilientClient(CountingHandler handler)
    {
        const string clientName = "probe";
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.AddServiceDefaults();
        builder.Services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        builder.Services.Configure<HttpStandardResilienceOptions>(
            clientName + "-standard",
            options =>
            {
                options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                options.Retry.UseJitter = false;
            });
        var app = builder.Build();
        return app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
    }

    private sealed class CountingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    private static WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.AddServiceDefaults();
        return builder.Build();
    }

    private static IEnumerable<string> RoutePatterns(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty);

    private static RouteEndpoint FindEndpoint(WebApplication app, string pattern) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == pattern);
}
