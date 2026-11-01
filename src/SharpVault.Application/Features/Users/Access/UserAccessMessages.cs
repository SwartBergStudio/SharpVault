// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Users.Access;

internal static class UserAccessMessages
{
    public const string CacheTtlOutOfRange = "UserAccess:CacheTtl must be between 0 and 30 seconds.";

    public const string CacheMaxEntriesOutOfRange = "UserAccess:CacheMaxEntries must be between 100 and 1000000.";

    public const string SignInRecordIntervalOutOfRange = "UserAccess:SignInRecordInterval must be between 1 minute and 24 hours.";

    public const string UserRowMissing = "The user row disappeared during the access check.";
}
