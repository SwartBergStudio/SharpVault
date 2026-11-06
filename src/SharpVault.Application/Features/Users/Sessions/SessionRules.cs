// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;

namespace SharpVault.Application.Features.Users.Sessions;

public static partial class SessionRules
{
    public const string InvalidSessionMessage = "The session reference is not valid.";

    public const string ExpiryTooFarMessage = "The session expiry is too far ahead.";

    public static readonly TimeSpan MaxExpiryAhead = TimeSpan.FromHours(9);

    public static bool IsValidSessionId(string? sessionId) => sessionId is not null && SessionIdPattern().IsMatch(sessionId);

    [GeneratedRegex("^[0-9a-f]{32}\\z")]
    private static partial Regex SessionIdPattern();
}
