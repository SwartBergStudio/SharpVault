// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.PasswordGeneration;

/// <summary>Constant validation and failure messages. None of them echoes an option value.</summary>
public static class GeneratorMessages
{
    public const string LengthOutOfRange = "Length must be between 8 and 128.";

    public const string NoCharacterSet = "Choose at least one character set.";

    public const string UndefinedCharacterSets = "The character sets are not valid.";

    public const string LengthBelowSetCount = "Length must be at least the number of chosen character sets.";

    public const string WordCountOutOfRange = "Word count must be between 3 and 20.";

    public const string UndefinedSeparator = "The separator is not valid.";

    public const string WordListCorrupt = "The embedded word list is damaged.";

    public const string RequireEverySetExhausted = "A password with every chosen set could not be generated.";
}
