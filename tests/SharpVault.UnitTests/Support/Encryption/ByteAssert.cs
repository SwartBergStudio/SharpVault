// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support.Encryption;

internal static class ByteAssert
{
    public static void AllZero(byte[] value)
    {
        Assert.NotNull(value);
        Assert.All(value, b => Assert.Equal(0, b));
    }

    public static void NotAllZero(byte[] value)
    {
        Assert.NotNull(value);
        Assert.Contains(value, b => b != 0);
    }
}
