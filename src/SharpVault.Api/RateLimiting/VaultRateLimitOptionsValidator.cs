// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;

namespace SharpVault.Api.RateLimiting;

public sealed class VaultRateLimitOptionsValidator : IValidateOptions<VaultRateLimitOptions>
{
    internal const int MinimumLimit = 1;
    internal const int MaximumLimit = 100_000;
    internal const string RequestsInvalid = "VaultRateLimits:RequestsPerMinute must be between 1 and 100000.";
    internal const string RevealsInvalid = "VaultRateLimits:RevealsPerMinute must be between 1 and 100000.";
    internal const string ShareWritesInvalid = "VaultRateLimits:ShareWritesPerMinute must be between 1 and 100000.";
    internal const string DirectorySearchesInvalid = "VaultRateLimits:DirectorySearchesPerMinute must be between 1 and 100000.";
    internal const string AdminActionsInvalid = "VaultRateLimits:AdminActionsPerMinute must be between 1 and 100000.";
    internal const string OneTimeLinkOpensInvalid = "VaultRateLimits:OneTimeLinkOpensPerMinute must be between 1 and 100000.";

    public ValidateOptionsResult Validate(string? name, VaultRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.RequestsPerMinute is < MinimumLimit or > MaximumLimit)
        {
            failures.Add(RequestsInvalid);
        }

        if (options.RevealsPerMinute is < MinimumLimit or > MaximumLimit)
        {
            failures.Add(RevealsInvalid);
        }

        if (options.ShareWritesPerMinute is < MinimumLimit or > MaximumLimit)
        {
            failures.Add(ShareWritesInvalid);
        }

        if (options.AdminActionsPerMinute is < MinimumLimit or > MaximumLimit)
        {
            failures.Add(AdminActionsInvalid);
        }

        if (options.DirectorySearchesPerMinute is < MinimumLimit or > MaximumLimit)
        {
            failures.Add(DirectorySearchesInvalid);
        }

        if (options.OneTimeLinkOpensPerMinute is < MinimumLimit or > MaximumLimit)
        {
            failures.Add(OneTimeLinkOpensInvalid);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
