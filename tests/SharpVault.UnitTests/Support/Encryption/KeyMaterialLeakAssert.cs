// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Security.Cryptography;

namespace SharpVault.UnitTests.Support.Encryption;

internal static class KeyMaterialLeakAssert
{
    public static void NotLeaked(string text, params byte[][] materials)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var material in materials)
        {
            Assert.NotEmpty(material);
            foreach (var form in Encodings(material))
            {
                Assert.DoesNotContain(form, text, StringComparison.Ordinal);
            }
        }
    }

    public static byte[] FromBase64Url(string value) => Base64Url.DecodeFromChars(value);

    public static string ToBase64Url(byte[] value) => Base64Url.EncodeToString(value);

    private static IEnumerable<string> Encodings(byte[] material)
    {
        yield return Convert.ToHexString(material);
        yield return Convert.ToHexStringLower(material);
        yield return Convert.ToBase64String(material);
        yield return Base64Url.EncodeToString(material);
    }
}
