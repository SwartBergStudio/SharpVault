// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.PasswordGeneration;

internal static class ChiSquare
{
    public static double Statistic(IEnumerable<int> observed, double expected) =>
        observed.Sum(count => (count - expected) * (count - expected) / expected);
}
