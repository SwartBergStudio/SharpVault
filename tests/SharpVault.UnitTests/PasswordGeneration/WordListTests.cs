// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SharpVault.PasswordGeneration;

namespace SharpVault.UnitTests.PasswordGeneration;

public sealed partial class WordListTests
{
    private const string ResourceName = "SharpVault.PasswordGeneration.Wordlists.eff_large_wordlist.txt";
    private const string ExpectedSha256 = "addd35536511597a02fa0a9ff1e5284677b8883b83e986e43f15a3db996b903e";

    [GeneratedRegex(@"^[a-z-]{3,9}\z")]
    private static partial Regex WordShape();

    [Fact]
    public void WordList_Embedded_Has7776UniqueWordsAndExpectedHash()
    {
        using var stream = typeof(PasswordGenerator).Assembly.GetManifestResourceStream(ResourceName);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        Assert.Equal(ExpectedSha256, Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())));
        Assert.Equal(7776, EffWordList.Count);
        Assert.Equal(7776, Enumerable.Range(0, EffWordList.Count).Select(EffWordList.At).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void WordList_Words_AreLowercaseLettersOrHyphen3To9Chars()
    {
        var offenders = Enumerable.Range(0, EffWordList.Count)
            .Select(EffWordList.At)
            .Where(word => !WordShape().IsMatch(word))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void WordList_FirstAndLast_AreAbacusAndZoom()
    {
        Assert.Equal("abacus", EffWordList.At(0));
        Assert.Equal("zoom", EffWordList.At(EffWordList.Count - 1));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void WordList_HashCheck_IsPerformedOnLoad()
    {
        var expectedHash = (string)typeof(EffWordList)
            .GetField("ExpectedSha256", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        using var stream = typeof(PasswordGenerator).Assembly.GetManifestResourceStream(ResourceName);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var load = typeof(EffWordList).GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())), expectedHash);
        Assert.Equal(7776, ((string[])load.Invoke(null, null)!).Length);
    }

    [Theory]
    [InlineData("1\tabacus\nno-separator-line\n")]
    [InlineData("1\t\n")]
    [InlineData("11111\n")]
    public void WordList_CorruptedContent_FailsToParseWithTheDamagedMessage(string text)
    {
        var parse = typeof(EffWordList).GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!;

        var exception = Assert.Throws<TargetInvocationException>(() => parse.Invoke(null, [text]));

        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal(GeneratorMessages.WordListCorrupt, inner.Message);
    }
}
