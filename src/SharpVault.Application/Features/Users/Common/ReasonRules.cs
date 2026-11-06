// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;

namespace SharpVault.Application.Features.Users.Common;

public static class ReasonRules
{
    public const int MinLength = 20;

    public const int MaxLength = 1000;

    public static IRuleBuilderOptions<T, string> AdminReason<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.Cascade(CascadeMode.Stop)
            .Must((string value) => TrimmedLength(value) >= MinLength)
            .WithMessage(UserAdminMessages.ReasonTooShort)
            .Must((string value) => TrimmedLength(value) <= MaxLength)
            .WithMessage(UserAdminMessages.ReasonTooLong)
            .Must((string value) => HasWellFormedText(value))
            .WithMessage(UserAdminMessages.ReasonNotAllowed);

    private static int TrimmedLength(string? value) => value?.Trim().Length ?? 0;

    private static bool HasWellFormedText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                continue;
            }

            if (char.IsSurrogate(character) || (char.IsControl(character) && !IsAllowedControl(character)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedControl(char character) => character is '\r' or '\n' or '\t';
}
