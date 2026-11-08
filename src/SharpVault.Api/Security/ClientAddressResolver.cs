// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.Extensions.Options;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.Api.Security;

public sealed partial class ClientAddressResolver(
    IOptions<WebGatewayOptions> options,
    TimeProvider timeProvider,
    ILogger<ClientAddressResolver> logger)
{
    private static readonly object MemoKey = new();
    private static readonly string[] ProofHeaders = [WebGatewayHeaders.Time, WebGatewayHeaders.ClientIp, WebGatewayHeaders.Signature];

    public string? Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(MemoKey, out var memoized))
        {
            return (string?)memoized;
        }

        var address = ResolveOnce(context);
        context.Items[MemoKey] = address;
        return address;
    }

    private string? ResolveOnce(HttpContext context)
    {
        var connection = UserProfileText.NormalizeIpAddress(context.Connection.RemoteIpAddress?.ToString());
        var headers = context.Request.Headers;
        if (!ProofHeaders.Any(headers.ContainsKey))
        {
            return connection;
        }

        var hasUser = WebGatewayScope.TryReadUserId(context.User, out var userId);
        var proven = hasUser ? ReadVerifiedAddress(headers, userId) : null;
        if (proven is null)
        {
            LogProofRejected(logger, hasUser ? userId : null);
            return connection;
        }

        return UserProfileText.NormalizeIpAddress(proven);
    }

    private string? ReadVerifiedAddress(IHeaderDictionary headers, Guid userId)
    {
        var key = options.Value.NormalizedKey;
        if (key is null
            || !TryGetSingle(headers, WebGatewayHeaders.Time, out var time)
            || !TryGetSingle(headers, WebGatewayHeaders.ClientIp, out var address)
            || !TryGetSingle(headers, WebGatewayHeaders.Signature, out var signature)
            || address.Length > UserProfileText.IpAddressMaxLength
            || !long.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)
            || !WebGatewaySignature.Verify(
                key,
                WebGatewayPurposes.ClientAddress,
                WebGatewayScope.For(userId),
                unixSeconds,
                address,
                string.Empty,
                signature,
                timeProvider.GetUtcNow()))
        {
            return null;
        }

        return address;
    }

    private static bool TryGetSingle(IHeaderDictionary headers, string name, out string value)
    {
        value = string.Empty;
        if (!headers.TryGetValue(name, out var values) || values.Count != 1 || values[0] is not { } single)
        {
            return false;
        }

        value = single;
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An address proof was rejected for user {UserId}: the connection address is recorded")]
    private static partial void LogProofRejected(ILogger logger, Guid? userId);
}
