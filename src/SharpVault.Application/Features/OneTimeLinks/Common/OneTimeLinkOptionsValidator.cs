// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public sealed class OneTimeLinkOptionsValidator : IValidateOptions<OneTimeLinkOptions>
{
    private static readonly CompositeFormat RangeMessage = CompositeFormat.Parse(OneTimeLinkMessages.OptionRangeFormat);
    private static readonly TimeSpan MinMaxLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxMaxLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan MinPurgeInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxPurgeInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinPurgeLeaseDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxPurgeLeaseDuration = TimeSpan.FromMinutes(30);

    public ValidateOptionsResult Validate(string? name, OneTimeLinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        Require(options.MaxLifetime, MinMaxLifetime, MaxMaxLifetime, nameof(options.MaxLifetime), failures);
        Require(options.PurgeInterval, MinPurgeInterval, MaxPurgeInterval, nameof(options.PurgeInterval), failures);
        Require(options.PurgeLeaseDuration, MinPurgeLeaseDuration, MaxPurgeLeaseDuration, nameof(options.PurgeLeaseDuration), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(TimeSpan value, TimeSpan min, TimeSpan max, string key, List<string> failures)
    {
        if (value < min || value > max)
        {
            failures.Add(string.Format(CultureInfo.InvariantCulture, RangeMessage, key, min, max));
        }
    }
}
