// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.PasswordGeneration;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.PasswordGeneration;

public sealed partial class PasswordGeneratorTests
{
    private const double DigitsCriticalValue = 44.8;
    private const double AlphabetCriticalValue = 172.7;

    private static readonly CharacterSets[] Singles =
        [CharacterSets.Lowercase, CharacterSets.Uppercase, CharacterSets.Digits, CharacterSets.Symbols];

    [GeneratedRegex(@"\bRandom\b")]
    private static partial Regex SystemRandomUsage();

    public static TheoryData<CharacterSets> EveryCombination()
    {
        var data = new TheoryData<CharacterSets>();
        for (var combination = 1; combination <= 15; combination++)
        {
            data.Add((CharacterSets)combination);
        }

        return data;
    }

    [Fact]
    public void GeneratePassword_Default_Returns20CharactersFromAllSets()
    {
        var secret = PasswordGenerator.GeneratePassword(PasswordOptions.Default);

        Assert.Equal(20, secret.Value.Length);
        Assert.All(Singles, set => Assert.True(secret.Value.AsSpan().IndexOfAny(CharacterAlphabets.For(set, false)) >= 0));
        Assert.InRange(secret.EntropyBits, 130.9259 - 1e-3, 130.9259 + 1e-3);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(128)]
    public void GeneratePassword_LengthBounds_8And128_Succeed(int length)
    {
        var secret = PasswordGenerator.GeneratePassword(new PasswordOptions(length, CharacterSets.Lowercase, false, false));

        Assert.Equal(length, secret.Value.Length);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(129)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void GeneratePassword_LengthOutOfRange_ThrowsArgumentException(int length)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => PasswordGenerator.GeneratePassword(new PasswordOptions(length, CharacterSets.All, false, false)));

        Assert.StartsWith(GeneratorMessages.LengthOutOfRange, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratePassword_NoSets_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => PasswordGenerator.GeneratePassword(new PasswordOptions(20, CharacterSets.None, false, false)));

        Assert.StartsWith(GeneratorMessages.NoCharacterSet, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(-1)]
    public void GeneratePassword_UndefinedFlag_ThrowsArgumentException(int sets)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => PasswordGenerator.GeneratePassword(new PasswordOptions(20, (CharacterSets)sets, false, false)));

        Assert.StartsWith(GeneratorMessages.UndefinedCharacterSets, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratePassword_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PasswordGenerator.GeneratePassword(null!));
        Assert.Throws<ArgumentNullException>(() => PasswordGenerator.Validate((PasswordOptions)null!));
    }

    [Fact]
    public void GeneratePassword_RequireEverySet_EverySelectedSetPresentIn10000Draws()
    {
        var options = new PasswordOptions(8, CharacterSets.All, false, true);

        for (var draw = 0; draw < 10_000; draw++)
        {
            var value = PasswordGenerator.GeneratePassword(options).Value;
            Assert.All(Singles, set => Assert.True(value.AsSpan().IndexOfAny(CharacterAlphabets.For(set, false)) >= 0, value));
        }
    }

    [Fact]
    public void GeneratePassword_RequireEverySetLengthBelowSetCount_IsInvalid()
    {
        var messages = PasswordGenerator.Validate(new PasswordOptions(3, CharacterSets.All, false, true));

        Assert.Contains(GeneratorMessages.LengthBelowSetCount, messages);
        Assert.Contains(GeneratorMessages.LengthOutOfRange, messages);
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoMessages()
    {
        Assert.Empty(PasswordGenerator.Validate(PasswordOptions.Default));
    }

    [Fact]
    public void Validate_RequireEverySetOff_DoesNotComplainAboutSetCount()
    {
        var messages = PasswordGenerator.Validate(new PasswordOptions(8, CharacterSets.All, false, false));

        Assert.Empty(messages);
    }

    [Fact]
    public void Validate_Messages_NeverEchoTheOptions()
    {
        var messages = PasswordGenerator.Validate(new PasswordOptions(987654, (CharacterSets)4242, true, true));

        Assert.NotEmpty(messages);
        Assert.All(messages, message =>
        {
            Assert.DoesNotContain("987654", message, StringComparison.Ordinal);
            Assert.DoesNotContain("4242", message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void GeneratePassword_ExcludeAmbiguous_NeverContainsAmbiguousCharacters()
    {
        var options = new PasswordOptions(128, CharacterSets.All, true, true);

        for (var draw = 0; draw < 200; draw++)
        {
            var value = PasswordGenerator.GeneratePassword(options).Value;
            Assert.DoesNotContain(value, character => CharacterAlphabets.Ambiguous.Contains(character, StringComparison.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(EveryCombination))]
    public void GeneratePassword_OnlySelectedSets_NoOtherCharacters(CharacterSets sets)
    {
        var allowed = string.Concat(Singles.Where(set => sets.HasFlag(set)).Select(set => CharacterAlphabets.For(set, false)));

        var value = PasswordGenerator.GeneratePassword(new PasswordOptions(128, sets, false, true)).Value;

        Assert.All(value, character => Assert.Contains(character, allowed));
    }

    [Fact]
    public void GeneratePassword_TwoCalls_ProduceDifferentValues()
    {
        var first = PasswordGenerator.GeneratePassword(PasswordOptions.Default).Value;
        var second = PasswordGenerator.GeneratePassword(PasswordOptions.Default).Value;

        Assert.NotEqual(first, second);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GeneratedSecret_ToString_DoesNotContainValue()
    {
        var secret = new GeneratedSecret("hunter2-marker-value", 12.5);

        Assert.DoesNotContain("hunter2", secret.ToString(), StringComparison.Ordinal);
        Assert.Equal(nameof(GeneratedSecret), secret.ToString());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GeneratePassword_DigitsOnly200000Chars_ChiSquareBelowCritical()
    {
        var counts = new int[10];
        var options = new PasswordOptions(128, CharacterSets.Digits, false, false);
        var total = 0;
        while (total < 200_000)
        {
            foreach (var character in PasswordGenerator.GeneratePassword(options).Value)
            {
                counts[character - '0']++;
                total++;
            }
        }

        Assert.True(ChiSquare.Statistic(counts, total / 10d) < DigitsCriticalValue);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GeneratePassword_AllSets_EachSymbolFrequencyChiSquareBelowCritical()
    {
        var alphabet = string.Concat(Singles.Select(set => CharacterAlphabets.For(set, false)));
        var counts = new int[alphabet.Length];
        var options = new PasswordOptions(128, CharacterSets.All, false, false);
        var total = 0;
        while (total < 94_000)
        {
            foreach (var character in PasswordGenerator.GeneratePassword(options).Value)
            {
                counts[alphabet.IndexOf(character, StringComparison.Ordinal)]++;
                total++;
            }
        }

        Assert.True(ChiSquare.Statistic(counts, total / (double)alphabet.Length) < AlphabetCriticalValue);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GeneratePassword_RequireEverySet_PositionOfDigitIsUniform()
    {
        var options = new PasswordOptions(10, CharacterSets.Lowercase | CharacterSets.Digits, false, true);
        var positions = new int[10];
        var samples = 0;
        while (samples < 20_000)
        {
            var value = PasswordGenerator.GeneratePassword(options).Value;
            var digitIndexes = Enumerable.Range(0, value.Length).Where(index => char.IsAsciiDigit(value[index])).ToList();
            if (digitIndexes.Count != 1)
            {
                continue;
            }

            positions[digitIndexes[0]]++;
            samples++;
        }

        Assert.True(ChiSquare.Statistic(positions, samples / 10d) < DigitsCriticalValue);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void PasswordGenerationSource_ContainsNoSystemRandom()
    {
        var directory = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.PasswordGeneration");

        var files = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();
        var offenders = files.Where(path => SystemRandomUsage().IsMatch(File.ReadAllText(path))).ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }
}
