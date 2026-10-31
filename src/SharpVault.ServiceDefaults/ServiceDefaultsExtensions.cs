// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharpVault.ServiceDefaults;

public static class ServiceDefaultsExtensions
{
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string HostingDiagnosticsCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";
    private const string HttpClientCategory = "System.Net.Http.HttpClient";
    private const string UrlQueryTag = "url.query";
    private const string UrlFullTag = "url.full";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.SuppressQueryStringLogging();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        builder.Services.AddOutputCache(options => options.AddPolicy(
            HealthEndpoints.CachePolicyName,
            policy => policy.Expire(HealthEndpoints.CacheDuration).SetVaryByQuery([]).SetVaryByHost(false)));
        builder.Services.Configure<KestrelServerOptions>(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = RequestLimits.MaxRequestBodyBytes;
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = IsTracedRequest;
                    options.EnrichWithHttpRequest = (activity, _) => activity.SetTag(UrlQueryTag, null);
                })
                .AddHttpClientInstrumentation(options =>
                    options.EnrichWithHttpRequestMessage = (activity, request) =>
                        activity.SetTag(UrlFullTag, WithoutQuery(request.RequestUri))));

        if (!string.IsNullOrWhiteSpace(builder.Configuration[OtlpEndpointKey]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder SuppressQueryStringLogging<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddFilter(HostingDiagnosticsCategory, LogLevel.Warning);
        builder.Logging.AddFilter(HttpClientCategory, LogLevel.Warning);
        builder.Services.PostConfigure<LoggerFilterOptions>(RaiseQueryLoggingRules);
        return builder;
    }

    private static void RaiseQueryLoggingRules(LoggerFilterOptions options)
    {
        string[] categories = [HostingDiagnosticsCategory, HttpClientCategory];
        var providerRules = options.Rules
            .Where(rule => rule.ProviderName is not null && rule.LogLevel < LogLevel.Warning)
            .ToList();

        foreach (var category in categories)
        {
            for (var index = 0; index < options.Rules.Count; index++)
            {
                var rule = options.Rules[index];
                if (rule.CategoryName == category && rule.LogLevel < LogLevel.Warning)
                {
                    options.Rules[index] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName, LogLevel.Warning, rule.Filter);
                }
            }

            foreach (var rule in providerRules.Where(rule => CoversCategory(rule.CategoryName, category)))
            {
                options.Rules.Add(new LoggerFilterRule(rule.ProviderName, category, LogLevel.Warning, null));
            }
        }
    }

    private static bool CoversCategory(string? ruleCategory, string category) =>
        ruleCategory is null
        || ruleCategory == category
        || category.StartsWith(ruleCategory + ".", StringComparison.Ordinal);

    private static string? WithoutQuery(Uri? uri) =>
        uri is null || !uri.IsAbsoluteUri
            ? null
            : uri.GetLeftPart(UriPartial.Path);

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck(HealthEndpoints.SelfCheckName, () => HealthCheckResult.Healthy(), [HealthEndpoints.LiveTag]);
        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpoints.HealthPath)
            .AllowAnonymous()
            .CacheOutput(HealthEndpoints.CachePolicyName)
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));
        app.MapHealthChecks(HealthEndpoints.AlivePath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthEndpoints.LiveTag),
        })
            .AllowAnonymous()
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]));
        return app;
    }

    private static bool IsTracedRequest(HttpContext context)
    {
        var path = context.Request.Path;
        return !path.StartsWithSegments(HealthEndpoints.HealthPath, StringComparison.Ordinal)
            && !path.StartsWithSegments(HealthEndpoints.AlivePath, StringComparison.Ordinal);
    }
}
