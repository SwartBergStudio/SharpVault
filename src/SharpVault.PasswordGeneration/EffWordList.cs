// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SharpVault.PasswordGeneration;

internal static class EffWordList
{
    private const string ResourceName = "SharpVault.PasswordGeneration.Wordlists.eff_large_wordlist.txt";

    private const string ExpectedSha256 = "addd35536511597a02fa0a9ff1e5284677b8883b83e986e43f15a3db996b903e";

    private const int ExpectedWordCount = 7776;

    private const char ColumnSeparator = '\t';

    private static readonly Lazy<string[]> Words = new(Load);

    internal static int Count => Words.Value.Length;

    internal static string At(int index) => Words.Value[index];

    private static string[] Load()
    {
        var bytes = ReadResource();
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != ExpectedSha256)
        {
            throw new InvalidOperationException(GeneratorMessages.WordListCorrupt);
        }

        var words = Parse(Encoding.UTF8.GetString(bytes));
        if (words.Length != ExpectedWordCount || words.Distinct(StringComparer.Ordinal).Count() != ExpectedWordCount)
        {
            throw new InvalidOperationException(GeneratorMessages.WordListCorrupt);
        }

        return words;
    }

    private static byte[] ReadResource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(GeneratorMessages.WordListCorrupt);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string[] Parse(string text)
    {
        var words = new List<string>(ExpectedWordCount);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf(ColumnSeparator, StringComparison.Ordinal);
            if (separatorIndex < 0 || separatorIndex == line.Length - 1)
            {
                throw new InvalidOperationException(GeneratorMessages.WordListCorrupt);
            }

            words.Add(line[(separatorIndex + 1)..]);
        }

        return [.. words];
    }
}
