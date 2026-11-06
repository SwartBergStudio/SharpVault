// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.PasswordGeneration;

/// <summary>Options for a random password.</summary>
/// <param name="Length">The number of characters, between 8 and 128.</param>
/// <param name="Sets">The character sets to draw from.</param>
/// <param name="ExcludeAmbiguous">Whether to leave out characters that look alike.</param>
/// <param name="RequireEverySet">Whether the result must contain a character from every chosen set.</param>
public sealed record PasswordOptions(int Length, CharacterSets Sets, bool ExcludeAmbiguous, bool RequireEverySet)
{
    public static PasswordOptions Default { get; } = new(20, CharacterSets.All, false, true);
}
