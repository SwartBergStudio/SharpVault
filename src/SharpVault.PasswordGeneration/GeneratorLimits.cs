// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.PasswordGeneration;

/// <summary>The accepted ranges of generator options.</summary>
public static class GeneratorLimits
{
    public const int MinLength = 8;

    public const int MaxLength = 128;

    public const int MinWords = 3;

    public const int MaxWords = 20;

    public const int MaxRequireEverySetAttempts = 10_000;
}
