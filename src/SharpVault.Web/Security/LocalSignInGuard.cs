// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Authentication.Mode;

namespace SharpVault.Web.Security;

public static class LocalSignInGuard
{
    private static readonly string[] ForwardedHeaderNames =
    [
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
        "X-Original-For",
        "X-Original-Proto",
        "X-Original-Host",
        "Via",
        "X-Real-IP",
    ];

    private const string ForwardedHeadersEnabledKey = "FORWARDEDHEADERS_ENABLED";

    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    public static bool Allows(AuthenticationMode mode, IPAddress? remote, string? host, bool hasForwardedHeaders)
    {
        if (mode != AuthenticationMode.Local || hasForwardedHeaders)
        {
            return false;
        }

        if (remote is null || !IPAddress.IsLoopback(remote))
        {
            return false;
        }

        return IsLoopbackHost(host);
    }

    public static bool IsForwardedHeadersModeEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return bool.TryParse(configuration[ForwardedHeadersEnabledKey], out var enabled) && enabled;
    }

    public static bool HasForwardedHeaders(IHeaderDictionary headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        return ForwardedHeaderNames.Any(headers.ContainsKey);
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var name = StripPort(host.Trim());
        return LoopbackHosts.Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string StripPort(string host)
    {
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']', StringComparison.Ordinal);
            return close < 0 ? host : host[..(close + 1)];
        }

        var colon = host.LastIndexOf(':');
        return colon < 0 ? host : host[..colon];
    }
}
