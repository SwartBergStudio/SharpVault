// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.PasswordGeneration;

namespace SharpVault.UnitTests.PasswordGeneration;

public sealed class CharacterAlphabetsTests
{
    private static readonly CharacterSets[] Singles =
        [CharacterSets.Lowercase, CharacterSets.Uppercase, CharacterSets.Digits, CharacterSets.Symbols];

    [Fact]
    public void Alphabets_Sizes_Are26_26_10_32()
    {
        var sizes = Singles.Select(set => CharacterAlphabets.For(set, false).Length);

        Assert.Equal([26, 26, 10, 32], sizes);
    }

    [Fact]
    public void Alphabets_ExcludeAmbiguous_Sizes_Are25_24_8_31()
    {
        var sizes = Singles.Select(set => CharacterAlphabets.For(set, true).Length);

        Assert.Equal([25, 24, 8, 31], sizes);
    }

    [Fact]
    public void Alphabets_AreDisjoint()
    {
        var all = string.Concat(Singles.Select(set => CharacterAlphabets.For(set, false)));

        Assert.Equal(94, all.Distinct().Count());
        Assert.Equal(94, all.Length);
    }

    [Fact]
    public void Alphabets_ExcludeAmbiguous_ContainNoAmbiguousCharacter()
    {
        var all = string.Concat(Singles.Select(set => CharacterAlphabets.For(set, true)));

        Assert.DoesNotContain(all, character => CharacterAlphabets.Ambiguous.Contains(character, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CharacterSets.None)]
    [InlineData(CharacterSets.All)]
    [InlineData(CharacterSets.Lowercase | CharacterSets.Digits)]
    public void For_NotExactlyOneSet_ThrowsArgumentOutOfRange(CharacterSets sets)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterAlphabets.For(sets, false));
    }
}
