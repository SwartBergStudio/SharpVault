// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;

namespace SharpVault.Application.Features.Users.Access;

public sealed class UserAccessOptionsValidator : IValidateOptions<UserAccessOptions>
{
    public ValidateOptionsResult Validate(string? name, UserAccessOptions options)
    {
        var failures = new List<string>();

        if (options.CacheTtl < TimeSpan.Zero || options.CacheTtl > UserAccessOptions.MaxCacheTtl)
        {
            failures.Add(UserAccessMessages.CacheTtlOutOfRange);
        }

        if (options.CacheMaxEntries < UserAccessOptions.MinCacheMaxEntries
            || options.CacheMaxEntries > UserAccessOptions.MaxCacheMaxEntries)
        {
            failures.Add(UserAccessMessages.CacheMaxEntriesOutOfRange);
        }

        if (options.SignInRecordInterval < UserAccessOptions.MinSignInRecordInterval
            || options.SignInRecordInterval > UserAccessOptions.MaxSignInRecordInterval)
        {
            failures.Add(UserAccessMessages.SignInRecordIntervalOutOfRange);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
