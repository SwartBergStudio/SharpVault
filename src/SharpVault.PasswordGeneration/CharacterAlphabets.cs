// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.PasswordGeneration;

/// <summary>The alphabets of each character set.</summary>
public static class CharacterAlphabets
{
    public const string Lowercase = "abcdefghijklmnopqrstuvwxyz";

    public const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public const string Digits = "0123456789";

    public const string Symbols = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    public const string Ambiguous = "Il1O0|";

    /// <summary>Returns the alphabet of one character set, without the ambiguous characters when asked.</summary>
    /// <param name="characterSet">Exactly one defined character set.</param>
    /// <param name="excludeAmbiguous">Whether to remove characters that look alike.</param>
    public static string For(CharacterSets characterSet, bool excludeAmbiguous)
    {
        var alphabet = characterSet switch
        {
            CharacterSets.Lowercase => Lowercase,
            CharacterSets.Uppercase => Uppercase,
            CharacterSets.Digits => Digits,
            CharacterSets.Symbols => Symbols,
            _ => throw new ArgumentOutOfRangeException(nameof(characterSet)),
        };

        if (!excludeAmbiguous)
        {
            return alphabet;
        }

        return string.Concat(alphabet.Where(character => !Ambiguous.Contains(character, StringComparison.Ordinal)));
    }
}
