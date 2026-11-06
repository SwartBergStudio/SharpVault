// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.PasswordGeneration;

/// <summary>The character groups a generated password can draw from.</summary>
[Flags]
public enum CharacterSets
{
    None = 0,
    Lowercase = 1,
    Uppercase = 2,
    Digits = 4,
    Symbols = 8,
    All = Lowercase | Uppercase | Digits | Symbols,
}
