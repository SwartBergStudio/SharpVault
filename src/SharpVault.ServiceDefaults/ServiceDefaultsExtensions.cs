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
        string[] families = [HostingDiagnosticsCategory, HttpClientCategory];
        foreach (var family in families)
        {
            RaiseFamily(options, family);
        }
    }

    private static void RaiseFamily(LoggerFilterOptions options, string family)
    {
        var appendedProviders = new HashSet<string?>(StringComparer.OrdinalIgnoreCase);
        var originalCount = options.Rules.Count;
        for (var index = 0; index < originalCount; index++)
        {
            var rule = options.Rules[index];
            if (rule.LogLevel >= LogLevel.Warning)
            {
                continue;
            }

            var prefix = RulePrefix(rule.CategoryName);
            if (!family.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !prefix.StartsWith(family, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (prefix.Length >= family.Length)
            {
                options.Rules[index] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName, LogLevel.Warning, rule.Filter);
            }
            else if (appendedProviders.Add(rule.ProviderName))
            {
                options.Rules.Add(new LoggerFilterRule(rule.ProviderName, family, LogLevel.Warning, null));
            }
        }
    }

    private static string RulePrefix(string? categoryName)
    {
        if (categoryName is null)
        {
            return string.Empty;
        }

        var wildcardIndex = categoryName.IndexOf('*', StringComparison.Ordinal);
        return wildcardIndex < 0 ? categoryName : categoryName[..wildcardIndex];
    }

    private static string? WithoutQuery(Uri? uri) =>
        uri is null || !uri.IsAbsoluteUri
            ? null
            : uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);

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
