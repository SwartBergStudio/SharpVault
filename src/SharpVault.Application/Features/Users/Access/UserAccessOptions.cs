// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Users.Access;

public sealed class UserAccessOptions
{
    public const string SectionName = "UserAccess";

    public static readonly TimeSpan MaxCacheTtl = TimeSpan.FromSeconds(30);

    public const int MinCacheMaxEntries = 100;

    public const int MaxCacheMaxEntries = 1_000_000;

    public static readonly TimeSpan MinSignInRecordInterval = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan MaxSignInRecordInterval = TimeSpan.FromHours(24);

    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(5);

    public int CacheMaxEntries { get; set; } = 10_000;

    public TimeSpan SignInRecordInterval { get; set; } = TimeSpan.FromHours(1);
}
