// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.PasswordGeneration;

namespace SharpVault.UnitTests.PasswordGeneration;

public sealed class PassphraseGeneratorTests
{
    private const double WordIndexCriticalValue = 8382.2;

    private static readonly HashSet<string> Words =
        Enumerable.Range(0, EffWordList.Count).Select(EffWordList.At).ToHashSet(StringComparer.Ordinal);

    private static PassphraseOptions With(int wordCount = 6, PassphraseSeparator separator = PassphraseSeparator.Space, bool capitalize = false, bool includeNumber = false) =>
        new(wordCount, separator, capitalize, includeNumber);

    private static bool CanSplit(string text, char separator, int count)
    {
        if (count == 1)
        {
            return Words.Contains(text);
        }

        for (var index = text.IndexOf(separator, StringComparison.Ordinal); index >= 0; index = text.IndexOf(separator, index + 1))
        {
            if (Words.Contains(text[..index]) && CanSplit(text[(index + 1)..], separator, count - 1))
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void GeneratePassphrase_Default_SixWordsJoinedByHyphen()
    {
        var secret = PasswordGenerator.GeneratePassphrase(PassphraseOptions.Default);

        Assert.True(CanSplit(secret.Value, '-', 6), secret.Value);
        Assert.InRange(secret.EntropyBits, 77.5489 - 1e-3, 77.5489 + 1e-3);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(20)]
    public void GeneratePassphrase_WordCountBounds_3And20_Succeed(int count)
    {
        var secret = PasswordGenerator.GeneratePassphrase(With(count));

        Assert.Equal(count, secret.Value.Split(' ').Length);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(21)]
    [InlineData(0)]
    [InlineData(-5)]
    public void GeneratePassphrase_WordCount2Or21_Throws(int count)
    {
        var exception = Assert.Throws<ArgumentException>(() => PasswordGenerator.GeneratePassphrase(With(count)));

        Assert.StartsWith(GeneratorMessages.WordCountOutOfRange, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PassphraseSeparator.Hyphen, '-')]
    [InlineData(PassphraseSeparator.Space, ' ')]
    [InlineData(PassphraseSeparator.Period, '.')]
    [InlineData(PassphraseSeparator.Underscore, '_')]
    public void GeneratePassphrase_EverySeparator_UsesItsCharacter(PassphraseSeparator separator, char expected)
    {
        var value = PasswordGenerator.GeneratePassphrase(With(5, separator)).Value;

        Assert.True(CanSplit(value, expected, 5), value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void GeneratePassphrase_UndefinedSeparator_Throws(int separator)
    {
        var exception = Assert.Throws<ArgumentException>(() => PasswordGenerator.GeneratePassphrase(With(6, (PassphraseSeparator)separator)));

        Assert.StartsWith(GeneratorMessages.UndefinedSeparator, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratePassphrase_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PasswordGenerator.GeneratePassphrase(null!));
        Assert.Throws<ArgumentNullException>(() => PasswordGenerator.Validate((PassphraseOptions)null!));
    }

    [Fact]
    public void Validate_BothProblems_ReturnsBothMessages()
    {
        var messages = PasswordGenerator.Validate(With(1, (PassphraseSeparator)0));

        Assert.Equal([GeneratorMessages.WordCountOutOfRange, GeneratorMessages.UndefinedSeparator], messages);
    }

    [Fact]
    public void GeneratePassphrase_Capitalize_EveryWordStartsUppercase()
    {
        for (var draw = 0; draw < 200; draw++)
        {
            var words = PasswordGenerator.GeneratePassphrase(With(10, capitalize: true)).Value.Split(' ');

            Assert.All(words, word =>
            {
                Assert.True(char.IsAsciiLetterUpper(word[0]), word);
                Assert.Contains(char.ToLowerInvariant(word[0]) + word[1..], Words);
            });
        }
    }

    [Fact]
    public void GeneratePassphrase_IncludeNumber_ExactlyOneDigitAppendedToOneWord()
    {
        for (var draw = 0; draw < 500; draw++)
        {
            var secret = PasswordGenerator.GeneratePassphrase(With(6, includeNumber: true));
            var words = secret.Value.Split(' ');

            Assert.Single(words, word => char.IsAsciiDigit(word[^1]));
            Assert.Equal(1, secret.Value.Count(char.IsAsciiDigit));
            Assert.All(words, word => Assert.Contains(char.IsAsciiDigit(word[^1]) ? word[..^1] : word, Words));
            Assert.InRange(secret.EntropyBits, 83.4558 - 1e-3, 83.4558 + 1e-3);
        }
    }

    [Fact]
    public void GeneratePassphrase_Words_AllComeFromTheList()
    {
        var words = PasswordGenerator.GeneratePassphrase(With(20)).Value.Split(' ');

        Assert.All(words, word => Assert.Contains(word, Words));
    }

    [Fact]
    public void GeneratePassphrase_TwoCalls_ProduceDifferentValues()
    {
        Assert.NotEqual(
            PasswordGenerator.GeneratePassphrase(With(20)).Value,
            PasswordGenerator.GeneratePassphrase(With(20)).Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GeneratePassphrase_WordIndices_ChiSquareBelowCritical()
    {
        var indexes = Enumerable.Range(0, EffWordList.Count).ToDictionary(EffWordList.At, index => index, StringComparer.Ordinal);
        var counts = new int[EffWordList.Count];
        var options = With(20);

        for (var draw = 0; draw < 38_880; draw++)
        {
            foreach (var word in PasswordGenerator.GeneratePassphrase(options).Value.Split(' '))
            {
                counts[indexes[word]]++;
            }
        }

        Assert.True(ChiSquare.Statistic(counts, 38_880 * 20d / EffWordList.Count) < WordIndexCriticalValue);
    }
}
