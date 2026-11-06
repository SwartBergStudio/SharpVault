// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SharpVault.Authentication.Identity;

namespace SharpVault.Api.RateLimiting;

public static class VaultRateLimitingExtensions
{
    private const string UnknownPartition = "unknown";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IHostApplicationBuilder AddVaultRateLimiting(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<VaultRateLimitOptions>()
            .Bind(builder.Configuration.GetSection(VaultRateLimitOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<VaultRateLimitOptions>, VaultRateLimitOptionsValidator>();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRetryAfterAsync;
            options.AddPolicy(RateLimitPolicies.Vault, context => PartitionFor(context, limits => limits.RequestsPerMinute));
            options.AddPolicy(RateLimitPolicies.Reveal, context => PartitionFor(context, limits => limits.RevealsPerMinute));
            options.AddPolicy(RateLimitPolicies.Sharing, context => PartitionFor(context, limits => limits.ShareWritesPerMinute));
            options.AddPolicy(RateLimitPolicies.DirectorySearch, context => PartitionFor(context, limits => limits.DirectorySearchesPerMinute));
            options.AddPolicy(RateLimitPolicies.AdminActions, context => PartitionFor(context, limits => limits.AdminActionsPerMinute));
        });
        return builder;
    }

    private static RateLimitPartition<string> PartitionFor(HttpContext context, Func<VaultRateLimitOptions, int> permitLimit)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<VaultRateLimitOptions>>().Value;
        var key = context.User.FindFirst(EntraClaimTypes.ObjectId)?.Value ?? UnknownPartition;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit(limits),
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }

    private static ValueTask WriteRetryAfterAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    }
}
