// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;

namespace SharpVault.Infrastructure.Maintenance;

public sealed class RecycleBinPurgeOptionsValidator : IValidateOptions<RecycleBinPurgeOptions>
{
    internal const string InitialDelayInvalid = "RecycleBinPurge:InitialDelay must be between 0 and 1 hour.";
    internal const string IntervalInvalid = "RecycleBinPurge:Interval must be between 1 minute and 24 hours.";
    internal const string LeaseDurationInvalid = "RecycleBinPurge:LeaseDuration must be between 1 minute and 1 hour.";

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);
    private static readonly TimeSpan OneDay = TimeSpan.FromHours(24);

    public ValidateOptionsResult Validate(string? name, RecycleBinPurgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.InitialDelay < TimeSpan.Zero || options.InitialDelay > OneHour)
        {
            failures.Add(InitialDelayInvalid);
        }

        if (options.Interval < OneMinute || options.Interval > OneDay)
        {
            failures.Add(IntervalInvalid);
        }

        if (options.LeaseDuration < OneMinute || options.LeaseDuration > OneHour)
        {
            failures.Add(LeaseDurationInvalid);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
