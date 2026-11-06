// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.PasswordGeneration;

/// <summary>Options for a random passphrase.</summary>
/// <param name="WordCount">The number of words, between 3 and 20.</param>
/// <param name="Separator">The character between words.</param>
/// <param name="Capitalize">Whether each word starts with an upper-case letter.</param>
/// <param name="IncludeNumber">Whether one digit is appended to one random word.</param>
public sealed record PassphraseOptions(int WordCount, PassphraseSeparator Separator, bool Capitalize, bool IncludeNumber)
{
    public static PassphraseOptions Default { get; } = new(6, PassphraseSeparator.Hyphen, false, false);
}
