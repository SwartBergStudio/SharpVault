// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.PasswordGeneration;

/// <summary>A generated password or passphrase with its entropy.</summary>
/// <param name="Value">The generated text.</param>
/// <param name="EntropyBits">The exact entropy of the generation procedure in bits.</param>
public sealed record GeneratedSecret(string Value, double EntropyBits)
{
    public override string ToString() => nameof(GeneratedSecret);
}
