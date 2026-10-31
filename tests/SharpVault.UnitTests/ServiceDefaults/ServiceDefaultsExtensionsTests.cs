// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.ServiceDefaults;

namespace SharpVault.UnitTests.ServiceDefaults;

public sealed class ServiceDefaultsExtensionsTests
{
    [Fact]
    [Trait("Category", "Security")]
    public void AddServiceDefaults_Registered_HidesServerHeader()
    {
        using var app = BuildApp();

        var options = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.False(options.AddServerHeader);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AddServiceDefaults_Registered_LimitsRequestBodyToOneMebibyte()
    {
        using var app = BuildApp();

        var options = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.Equal(1_048_576, options.Limits.MaxRequestBodySize);
        Assert.Equal(RequestLimits.MaxRequestBodyBytes, options.Limits.MaxRequestBodySize);
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
    public void AddServiceDefaults_Registered_ReturnsSameBuilder()
    {
        var builder = WebApplication.CreateBuilder();

        var returned = builder.AddServiceDefaults();

        Assert.Same(builder, returned);
    }

    [Fact]
    public void AddServiceDefaults_OtlpEndpointNotConfigured_BuildsWithoutExporter()
    {
        var exception = Record.Exception(() =>
        {
            using var app = BuildApp();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void HealthEndpoints_Constants_HaveExpectedValues()
    {
        Assert.Equal("/health", HealthEndpoints.HealthPath);
        Assert.Equal("/alive", HealthEndpoints.AlivePath);
        Assert.Equal("live", HealthEndpoints.LiveTag);
        Assert.Equal("self", HealthEndpoints.SelfCheckName);
        Assert.Equal("health", HealthEndpoints.CachePolicyName);
        Assert.Equal(TimeSpan.FromSeconds(5), HealthEndpoints.CacheDuration);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RequestLimits_MaxRequestBodyBytes_IsOneMebibyte()
    {
        Assert.Equal(1024L * 1024L, RequestLimits.MaxRequestBodyBytes);
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

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/health")]
    [InlineData("/alive")]
    public void MapDefaultEndpoints_Mapped_EndpointIsAnonymous(string pattern)
    {
        using var app = BuildApp();
        app.MapDefaultEndpoints();

        var endpoint = FindEndpoint(app, pattern);

        Assert.NotNull(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/health")]
    [InlineData("/alive")]
    public void MapDefaultEndpoints_Mapped_EndpointAcceptsOnlyGetAndHead(string pattern)
    {
        using var app = BuildApp();
        app.MapDefaultEndpoints();

        var endpoint = FindEndpoint(app, pattern);

        var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["GET", "HEAD"], methods);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MapDefaultEndpoints_Mapped_HealthIsOutputCachedAndAliveIsNot()
    {
        using var app = BuildApp();
        app.MapDefaultEndpoints();

        var health = FindEndpoint(app, "/health");
        var alive = FindEndpoint(app, "/alive");

        Assert.Single(health.Metadata, IsOutputCachePolicy);
        Assert.DoesNotContain(alive.Metadata, IsOutputCachePolicy);
    }

    [Fact]
    public void MapDefaultEndpoints_Mapped_ReturnsSameApplication()
    {
        using var app = BuildApp();

        var returned = app.MapDefaultEndpoints();

        Assert.Same(app, returned);
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

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Microsoft", "Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("System", "System.Net.Http.HttpClient.Default.LogicalHandler")]
    public void SuppressQueryStringLogging_ProviderRulesDifferOnlyByCase_EffectiveLevelStaysWarning(string ancestor, string category)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.AddConsole();
        builder.Services.Configure<LoggerFilterOptions>(options =>
        {
            options.Rules.Add(new LoggerFilterRule("console", ancestor, LogLevel.Debug, null));
            options.Rules.Add(new LoggerFilterRule("Console", ancestor, LogLevel.Debug, null));
        });
        builder.AddServiceDefaults();
        using var app = builder.Build();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);

        Assert.False(logger.IsEnabled(LogLevel.Debug));
        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore.*", "Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Logging:Console:LogLevel:Microsoft.*", "Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Logging:Console:LogLevel:microsoft.aspnetcore", "Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Logging:LogLevel:System.Net.Http.HttpClient.Default", "System.Net.Http.HttpClient.Default.LogicalHandler")]
    [InlineData("Logging:Console:LogLevel:System.Net.Http.HttpClient.Default.LogicalHandler", "System.Net.Http.HttpClient.Default.LogicalHandler")]
    [InlineData("Logging:Console:LogLevel:System.Net.Http.*.LogicalHandler", "System.Net.Http.HttpClient.Default.LogicalHandler")]
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
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore.*", "Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore.Hosting.Lifetime", "Microsoft.AspNetCore.Hosting.Lifetime")]
    [InlineData("Logging:Console:LogLevel:System.Net.Http.HttpClientFactoryProbe", "System.Net.Http.HttpClientFactoryProbe")]
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

    private static bool IsOutputCachePolicy(object metadata) =>
        metadata.GetType().Namespace == "Microsoft.AspNetCore.OutputCaching";

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
