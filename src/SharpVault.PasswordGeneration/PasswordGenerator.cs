// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Security.Cryptography;

namespace SharpVault.PasswordGeneration;

/// <summary>Generates random passwords and passphrases with a cryptographic random number generator.</summary>
public static class PasswordGenerator
{
    private const int DigitCount = 10;

    private static readonly CharacterSets[] OrderedSets =
    [
        CharacterSets.Lowercase,
        CharacterSets.Uppercase,
        CharacterSets.Digits,
        CharacterSets.Symbols,
    ];

    /// <summary>Returns the messages for every problem in the options. An empty list means the options are valid.</summary>
    /// <param name="options">The password options.</param>
    public static IReadOnlyList<string> Validate(PasswordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var messages = new List<string>();
        if (options.Length is < GeneratorLimits.MinLength or > GeneratorLimits.MaxLength)
        {
            messages.Add(GeneratorMessages.LengthOutOfRange);
        }

        if (((int)options.Sets & ~(int)CharacterSets.All) != 0)
        {
            messages.Add(GeneratorMessages.UndefinedCharacterSets);
            return messages;
        }

        if (options.Sets == CharacterSets.None)
        {
            messages.Add(GeneratorMessages.NoCharacterSet);
            return messages;
        }

        if (options.RequireEverySet && options.Length < SelectedSets(options.Sets).Count())
        {
            messages.Add(GeneratorMessages.LengthBelowSetCount);
        }

        return messages;
    }

    /// <summary>Returns the messages for every problem in the options. An empty list means the options are valid.</summary>
    /// <param name="options">The passphrase options.</param>
    public static IReadOnlyList<string> Validate(PassphraseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var messages = new List<string>();
        if (options.WordCount is < GeneratorLimits.MinWords or > GeneratorLimits.MaxWords)
        {
            messages.Add(GeneratorMessages.WordCountOutOfRange);
        }

        if (!Enum.IsDefined(options.Separator))
        {
            messages.Add(GeneratorMessages.UndefinedSeparator);
        }

        return messages;
    }

    /// <summary>Generates a password.</summary>
    /// <param name="options">The password options.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    /// <exception cref="InvalidOperationException">No password with every chosen set was drawn within the attempt limit.</exception>
    public static GeneratedSecret GeneratePassword(PasswordOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = Validate(options);
        if (problems.Count > 0)
        {
            throw new ArgumentException(problems[0], nameof(options));
        }

        var alphabets = SelectedSets(options.Sets)
            .Select(set => CharacterAlphabets.For(set, options.ExcludeAmbiguous))
            .ToArray();
        var combined = string.Concat(alphabets);
        var value = options.RequireEverySet
            ? DrawWithEverySet(combined, alphabets, options.Length)
            : RandomNumberGenerator.GetString(combined, options.Length);
        var setSizes = alphabets.Select(alphabet => alphabet.Length).ToArray();
        return new GeneratedSecret(value, EntropyCalculator.PasswordBits(setSizes, options.Length, options.RequireEverySet));
    }

    /// <summary>Generates a passphrase from the EFF large word list.</summary>
    /// <param name="options">The passphrase options.</param>
    /// <exception cref="ArgumentException">The options are not valid.</exception>
    public static GeneratedSecret GeneratePassphrase(PassphraseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = Validate(options);
        if (problems.Count > 0)
        {
            throw new ArgumentException(problems[0], nameof(options));
        }

        var words = new string[options.WordCount];
        for (var index = 0; index < words.Length; index++)
        {
            var word = EffWordList.At(RandomNumberGenerator.GetInt32(EffWordList.Count));
            words[index] = options.Capitalize ? Capitalize(word) : word;
        }

        if (options.IncludeNumber)
        {
            var digit = RandomNumberGenerator.GetInt32(DigitCount).ToString(CultureInfo.InvariantCulture);
            words[RandomNumberGenerator.GetInt32(words.Length)] += digit;
        }

        var value = string.Join(SeparatorOf(options.Separator), words);
        return new GeneratedSecret(value, EntropyCalculator.PassphraseBits(options.WordCount, options.IncludeNumber));
    }

    /// <summary>Rates an entropy value.</summary>
    /// <param name="bits">The entropy in bits.</param>
    public static PasswordStrength StrengthOf(double bits) => EntropyCalculator.StrengthOf(bits);

    private static IEnumerable<CharacterSets> SelectedSets(CharacterSets sets) =>
        OrderedSets.Where(set => sets.HasFlag(set));

    private static string DrawWithEverySet(string combined, string[] alphabets, int length)
    {
        for (var attempt = 0; attempt < GeneratorLimits.MaxRequireEverySetAttempts; attempt++)
        {
            var candidate = RandomNumberGenerator.GetString(combined, length);
            if (alphabets.All(alphabet => candidate.AsSpan().IndexOfAny(alphabet) >= 0))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(GeneratorMessages.RequireEverySetExhausted);
    }

    private static string Capitalize(string word) =>
        string.Concat(char.ToUpperInvariant(word[0]).ToString(), word.AsSpan(1));

    private static string SeparatorOf(PassphraseSeparator separator) => separator switch
    {
        PassphraseSeparator.Hyphen => "-",
        PassphraseSeparator.Space => " ",
        PassphraseSeparator.Period => ".",
        PassphraseSeparator.Underscore => "_",
        _ => throw new ArgumentOutOfRangeException(nameof(separator)),
    };
}
