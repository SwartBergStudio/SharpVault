// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using FluentValidation;

namespace SharpVault.Application.Features.Vault.Common;

public static partial class ETagRules
{
    public static IRuleBuilderOptions<T, TProperty> ValidIfMatch<T, TProperty>(this IRuleBuilder<T, TProperty> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Must(value => IsValid(value as string)).WithMessage(VaultMessages.InvalidETag);
    }

    public static bool IsValid(string? value) =>
        value is { Length: > 0 } && value.Length <= VaultLimits.ETagMaxLength && ETagPattern().IsMatch(value);

    [GeneratedRegex("^W/\"datetime'[0-9A-Za-z%.:\\-]+'\"\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ETagPattern();
}
