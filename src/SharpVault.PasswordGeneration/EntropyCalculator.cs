// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Numerics;

namespace SharpVault.PasswordGeneration;

/// <summary>Computes the entropy of the generation procedures.</summary>
public static class EntropyCalculator
{
    private const double BinaryBase = 2;

    /// <summary>Returns the entropy of a password in bits.</summary>
    /// <param name="setSizes">The size of the alphabet of every chosen set.</param>
    /// <param name="length">The password length.</param>
    /// <param name="requireEverySet">Whether every set must appear, which excludes the passwords that miss one.</param>
    public static double PasswordBits(IReadOnlyList<int> setSizes, int length, bool requireEverySet)
    {
        ArgumentNullException.ThrowIfNull(setSizes);
        var alphabetSize = setSizes.Sum();
        if (!requireEverySet)
        {
            return length * Math.Log2(alphabetSize);
        }

        return BigInteger.Log(CountWithEverySet(setSizes, alphabetSize, length), BinaryBase);
    }

    /// <summary>Returns the entropy of a passphrase in bits.</summary>
    /// <param name="wordCount">The number of words.</param>
    /// <param name="includeNumber">Whether a digit is appended to one word.</param>
    public static double PassphraseBits(int wordCount, bool includeNumber)
    {
        var bits = wordCount * Math.Log2(EffWordList.Count);
        return includeNumber ? bits + Math.Log2(10d * wordCount) : bits;
    }

    /// <summary>Rates an entropy value.</summary>
    /// <param name="bits">The entropy in bits.</param>
    public static PasswordStrength StrengthOf(double bits) => bits switch
    {
        < 40 => PasswordStrength.VeryWeak,
        < 60 => PasswordStrength.Weak,
        < 80 => PasswordStrength.Reasonable,
        < 100 => PasswordStrength.Strong,
        _ => PasswordStrength.VeryStrong,
    };

    private static BigInteger CountWithEverySet(IReadOnlyList<int> setSizes, int alphabetSize, int length)
    {
        var total = BigInteger.Zero;
        var subsetCount = 1 << setSizes.Count;
        for (var mask = 0; mask < subsetCount; mask++)
        {
            var excluded = 0;
            var members = 0;
            for (var index = 0; index < setSizes.Count; index++)
            {
                if ((mask & (1 << index)) != 0)
                {
                    excluded += setSizes[index];
                    members++;
                }
            }

            var term = BigInteger.Pow(alphabetSize - excluded, length);
            total += members % 2 == 0 ? term : -term;
        }

        return total;
    }
}
