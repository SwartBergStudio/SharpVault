// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;

namespace SharpVault.Application.Features.Transfers.Common;

public sealed class TransferOptionsValidator : IValidateOptions<TransferOptions>
{
    private static readonly TimeSpan MinRequestLifetime = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxRequestLifetime = TimeSpan.FromDays(90);
    private static readonly TimeSpan MinPollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MinLeaseDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxLeaseDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinMaxBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxMaxBackoff = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, TransferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        Require(options.RequestLifetime, MinRequestLifetime, MaxRequestLifetime, nameof(options.RequestLifetime), failures);
        Require(options.PollInterval, MinPollInterval, MaxPollInterval, nameof(options.PollInterval), failures);
        Require(options.LeaseDuration, MinLeaseDuration, MaxLeaseDuration, nameof(options.LeaseDuration), failures);
        Require(options.MaxBackoff, MinMaxBackoff, MaxMaxBackoff, nameof(options.MaxBackoff), failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Require(TimeSpan value, TimeSpan min, TimeSpan max, string key, List<string> failures)
    {
        if (value < min || value > max)
        {
            failures.Add($"Transfers:{key} must be between {min} and {max}.");
        }
    }
}
