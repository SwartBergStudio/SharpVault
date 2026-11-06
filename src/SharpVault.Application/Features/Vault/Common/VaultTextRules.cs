// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text;
using FluentValidation;

namespace SharpVault.Application.Features.Vault.Common;

public static class VaultTextRules
{
    private const char BidiEmbeddingStart = '‪';
    private const char BidiEmbeddingEnd = '‮';
    private const char BidiIsolateStart = '⁦';
    private const char BidiIsolateEnd = '⁩';

    public static IRuleBuilderOptions<T, TProperty> SingleLineText<T, TProperty>(this IRuleBuilder<T, TProperty> rule, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule
            .Must(value => !string.IsNullOrWhiteSpace(value as string)).WithMessage(VaultMessages.FieldRequired)
            .Must(value => LengthAtMost(value as string, maxLength)).WithMessage(VaultMessages.FieldTooLong)
            .Must(value => IsWellFormed(value as string)).WithMessage(VaultMessages.NotWellFormedText)
            .Must(value => HasOnlyAllowedCharacters(value as string)).WithMessage(VaultMessages.InvalidCharacters);
    }

    public static IRuleBuilderOptions<T, TProperty> MultiLineSecretText<T, TProperty>(this IRuleBuilder<T, TProperty> rule, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule
            .Must(value => !string.IsNullOrEmpty(value as string)).WithMessage(VaultMessages.FieldRequired)
            .Must(value => value is not string text || text.Length <= maxLength).WithMessage(VaultMessages.FieldTooLong)
            .Must(value => IsWellFormed(value as string)).WithMessage(VaultMessages.NotWellFormedText);
    }

    public static IRuleBuilderOptions<T, TProperty> HttpUrl<T, TProperty>(this IRuleBuilder<T, TProperty> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Must(value => IsHttpUrl(value as string)).WithMessage(VaultMessages.InvalidUrl);
    }

    public static IRuleBuilderOptions<T, TProperty> TagList<T, TProperty>(this IRuleBuilder<T, TProperty> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule
            .Must(value => TagsOf(value).Count <= VaultLimits.MaxTags).WithMessage(VaultMessages.TooManyTags)
            .Must(value => TagsOf(value).All(tag => !string.IsNullOrWhiteSpace(tag))).WithMessage(VaultMessages.FieldRequired)
            .Must(value => TagsOf(value).All(tag => LengthAtMost(tag, VaultLimits.TagMaxLength))).WithMessage(VaultMessages.FieldTooLong)
            .Must(value => TagsOf(value).All(IsWellFormed)).WithMessage(VaultMessages.NotWellFormedText)
            .Must(value => TagsOf(value).All(HasOnlyAllowedCharacters)).WithMessage(VaultMessages.InvalidCharacters)
            .Must(value => AreDistinct(TagsOf(value))).WithMessage(VaultMessages.DuplicateTag);
    }

    private static List<string?> TagsOf<TProperty>(TProperty value) =>
        value is IEnumerable<string?> tags ? [.. tags] : [];

    private static bool AreDistinct(List<string?> tags)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return tags.All(tag => tag is null || seen.Add(tag.Trim()));
    }

    private static bool LengthAtMost(string? value, int maxLength) => value is null || value.Trim().Length <= maxLength;

    private static bool IsWellFormed(string? value)
    {
        if (value is null)
        {
            return true;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasOnlyAllowedCharacters(string? value)
    {
        if (value is null)
        {
            return true;
        }

        foreach (var character in value)
        {
            if (char.GetUnicodeCategory(character) == UnicodeCategory.Control || IsBidiControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBidiControl(char character) =>
        character is >= BidiEmbeddingStart and <= BidiEmbeddingEnd or >= BidiIsolateStart and <= BidiIsolateEnd;

    private static bool ContainsFormatCharacter(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHttpUrl(string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value.Any(char.IsWhiteSpace) || !HasOnlyAllowedCharacters(value) || ContainsFormatCharacter(value))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
