// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;

namespace SharpVault.UI.Shared.Services.Idle;

public sealed class IdleLockOptionsValidator : IValidateOptions<IdleLockOptions>
{
    public ValidateOptionsResult Validate(string? name, IdleLockOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Timeout < IdleLockOptions.MinimumTimeout || options.Timeout > IdleLockOptions.MaximumTimeout)
        {
            return ValidateOptionsResult.Fail(
                $"{IdleLockOptions.SectionName}:{nameof(IdleLockOptions.Timeout)} must be between 1 minute and 8 hours.");
        }

        return ValidateOptionsResult.Success;
    }
}
