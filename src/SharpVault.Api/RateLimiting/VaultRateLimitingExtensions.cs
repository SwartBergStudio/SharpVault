// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SharpVault.Api.Security;
using SharpVault.Authentication.Identity;

namespace SharpVault.Api.RateLimiting;

public static class VaultRateLimitingExtensions
{
    private const string UnknownPartition = "unknown";
    private const int Ipv6PrefixBytes = 8;

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
            options.AddPolicy(RateLimitPolicies.OneTimeLinkOpen, OneTimeLinkOpenPartitionFor);
        });
        return builder;
    }

    private static RateLimitPartition<string> PartitionFor(HttpContext context, Func<VaultRateLimitOptions, int> permitLimit)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<VaultRateLimitOptions>>().Value;
        var key = context.User.FindFirst(EntraClaimTypes.ObjectId)?.Value ?? UnknownPartition;
        return FixedWindowFor(key, permitLimit(limits));
    }

    private static RateLimitPartition<string> OneTimeLinkOpenPartitionFor(HttpContext context)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<VaultRateLimitOptions>>().Value;
        return FixedWindowFor(ClientAddressPartition(context.User), limits.OneTimeLinkOpensPerMinute);
    }

    private static RateLimitPartition<string> FixedWindowFor(string key, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = Window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static string ClientAddressPartition(ClaimsPrincipal user)
    {
        if (!IPAddress.TryParse(user.FindFirst(LinkGatewayClaims.ClientIp)?.Value, out var address))
        {
            return UnknownPartition;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 ? Ipv6Prefix(address) : address.ToString();
    }

    private static string Ipv6Prefix(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6PrefixBytes, bytes.Length - Ipv6PrefixBytes);
        return new IPAddress(bytes).ToString();
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
