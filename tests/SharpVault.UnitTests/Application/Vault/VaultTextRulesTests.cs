// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class VaultTextRulesTests
{
    private const int Max = 10;

    private static readonly SingleLineValidator SingleLine = new(Max);
    private static readonly MultiLineValidator MultiLine = new(Max);
    private static readonly UrlValidator Url = new();
    private static readonly TagsValidator Tags = new();

    private static IEnumerable<string> Messages(FluentValidation.Results.ValidationResult result) =>
        result.Errors.Select(error => error.ErrorMessage);

    [Fact]
    public void SingleLineText_AtMax_Passes()
    {
        Assert.True(SingleLine.Validate(new TextModel(new string('a', Max))).IsValid);
    }

    [Fact]
    public void SingleLineText_AtMaxAfterTrim_Passes()
    {
        Assert.True(SingleLine.Validate(new TextModel("  " + new string('a', Max) + "  ")).IsValid);
    }

    [Fact]
    public void SingleLineText_OverMax_FailsWithConstantMessage()
    {
        var result = SingleLine.Validate(new TextModel(new string('a', Max + 1)));

        Assert.Contains(VaultMessages.FieldTooLong, Messages(result));
    }

    [Fact]
    public void SingleLineText_WhitespaceOnly_Fails()
    {
        var result = SingleLine.Validate(new TextModel("   "));

        Assert.Contains(VaultMessages.FieldRequired, Messages(result));
    }

    [Fact]
    public void SingleLineText_Null_Fails()
    {
        var result = SingleLine.Validate(new TextModel(null));

        Assert.False(result.IsValid);
        Assert.Contains(VaultMessages.FieldRequired, Messages(result));
    }

    [Theory]
    [InlineData("a\u0000b")]
    [InlineData("a\u001bb")]
    [InlineData("a\u0085b")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    public void SingleLineText_ControlCharacter_Fails(string value)
    {
        var result = SingleLine.Validate(new TextModel(value));

        Assert.Contains(VaultMessages.InvalidCharacters, Messages(result));
    }

    [Theory]
    [InlineData('‮')]
    [InlineData('⁦')]
    public void SingleLineText_BidiControl_Fails(char bidi)
    {
        var result = SingleLine.Validate(new TextModel("a" + bidi + "b"));

        Assert.Contains(VaultMessages.InvalidCharacters, Messages(result));
    }

    [Fact]
    public void SingleLineText_LoneSurrogate_Fails()
    {
        string[] samples = ["a\ud800b", "ab\ud83d"];

        foreach (var value in samples)
        {
            var result = SingleLine.Validate(new TextModel(value));

            Assert.Contains(VaultMessages.NotWellFormedText, Messages(result));
        }
    }

    [Fact]
    public void SingleLineText_EmojiSurrogatePairAndOrdinaryUnicode_Passes()
    {
        Assert.True(SingleLine.Validate(new TextModel("a\U0001F600b")).IsValid);
        Assert.True(SingleLine.Validate(new TextModel("Zoë 名前")).IsValid);
    }

    [Fact]
    public void SingleLineText_ZeroWidthJoiner_PassesForNames()
    {
        Assert.True(SingleLine.Validate(new TextModel("name‍x")).IsValid);
    }

    [Fact]
    public void MultiLineSecretText_NewlinesAndTabs_Pass()
    {
        var roomy = new MultiLineValidator(200);

        Assert.True(roomy.Validate(new TextModel("line one\r\nline two\n\tindented")).IsValid);
    }

    [Fact]
    public void MultiLineSecretText_AtMax_Passes()
    {
        Assert.True(MultiLine.Validate(new TextModel(new string('x', Max))).IsValid);
    }

    [Fact]
    public void MultiLineSecretText_OverMax_FailsWithConstantMessage()
    {
        var result = MultiLine.Validate(new TextModel(new string('x', Max + 1)));

        Assert.Contains(VaultMessages.FieldTooLong, Messages(result));
    }

    [Fact]
    public void MultiLineSecretText_LoneSurrogate_Fails()
    {
        var result = MultiLine.Validate(new TextModel("a\ud800"));

        Assert.Contains(VaultMessages.NotWellFormedText, Messages(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MultiLineSecretText_EmptyOrNull_Fails(string? value)
    {
        var result = MultiLine.Validate(new TextModel(value));

        Assert.Contains(VaultMessages.FieldRequired, Messages(result));
    }

    [Fact]
    public void MultiLineSecretText_WhitespaceOnly_IsAllowedBecauseSecretsMayBeSpaces()
    {
        Assert.True(MultiLine.Validate(new TextModel("   ")).IsValid);
    }

    [Theory]
    [InlineData("http://example.test/path?x=1#y")]
    [InlineData("HTTPS://EXAMPLE.TEST")]
    public void HttpUrl_HttpsAndHttp_Pass(string value)
    {
        Assert.True(Url.Validate(new TextModel(value)).IsValid);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    [InlineData("example.test")]
    [InlineData("")]
    public void HttpUrl_NonHttpSchemeRelativeOrEmpty_Fails(string value)
    {
        var result = Url.Validate(new TextModel(value));

        Assert.Contains(VaultMessages.InvalidUrl, Messages(result));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("https://example.test/a b")]
    [InlineData(" https://example.test/")]
    [InlineData("https://example.test/\n")]
    [InlineData("https://example.test/\u0000")]
    public void HttpUrl_WithWhitespaceOrControl_Fails(string value)
    {
        var result = Url.Validate(new TextModel(value));

        Assert.Contains(VaultMessages.InvalidUrl, Messages(result));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData('‮')]
    [InlineData('⁦')]
    public void HttpUrl_BidiControl_Fails(char bidi)
    {
        var result = Url.Validate(new TextModel("https://example.test/a" + bidi + "b"));

        Assert.Contains(VaultMessages.InvalidUrl, Messages(result));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("​")]
    [InlineData("­")]
    [InlineData("󠁁")]
    public void HttpUrl_FormatCharacter_Fails(string formatCharacter)
    {
        var result = Url.Validate(new TextModel("https://example.test/a" + formatCharacter + "b"));

        Assert.Contains(VaultMessages.InvalidUrl, Messages(result));
    }

    [Fact]
    public void HttpUrl_Null_PassesBecauseTheFieldIsOptional()
    {
        Assert.True(Url.Validate(new TextModel(null)).IsValid);
    }

    [Fact]
    public void TagList_TwentyTags_Passes()
    {
        var tags = Enumerable.Range(0, VaultLimits.MaxTags).Select(index => "tag" + index).ToList();

        Assert.True(Tags.Validate(new TagModel(tags)).IsValid);
    }

    [Fact]
    public void TagList_TwentyOne_Fails()
    {
        var tags = Enumerable.Range(0, VaultLimits.MaxTags + 1).Select(index => "tag" + index).ToList();

        Assert.Contains(VaultMessages.TooManyTags, Messages(Tags.Validate(new TagModel(tags))));
    }

    [Theory]
    [InlineData("Prod", "prod")]
    [InlineData("prod", "  PROD ")]
    public void TagList_DuplicateCaseOrTrimmed_Fails(string first, string second)
    {
        Assert.Contains(VaultMessages.DuplicateTag, Messages(Tags.Validate(new TagModel([first, second]))));
    }

    [Fact]
    public void TagList_WhitespaceOnlyTag_Fails()
    {
        Assert.Contains(VaultMessages.FieldRequired, Messages(Tags.Validate(new TagModel(["ok", "   "]))));
    }

    [Fact]
    public void TagList_NullElement_Fails()
    {
        var result = Tags.Validate(new TagModel(["ok", null!]));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void TagList_TagOverFiftyCharacters_Fails()
    {
        Assert.Contains(VaultMessages.FieldTooLong, Messages(Tags.Validate(new TagModel([new string('t', VaultLimits.TagMaxLength + 1)]))));
    }

    [Fact]
    public void TagList_TagWithControlOrBidiCharacters_Fails()
    {
        Assert.Contains(VaultMessages.InvalidCharacters, Messages(Tags.Validate(new TagModel(["a\nb"]))));
        Assert.Contains(VaultMessages.InvalidCharacters, Messages(Tags.Validate(new TagModel(["a‮b"]))));
    }

    [Fact]
    public void TagList_Empty_Passes()
    {
        Assert.True(Tags.Validate(new TagModel([])).IsValid);
    }

    [Fact]
    public void TagList_LoneSurrogate_Fails()
    {
        Assert.Contains(VaultMessages.NotWellFormedText, Messages(Tags.Validate(new TagModel(["a\ud800"]))));
    }

    private sealed record TextModel(string? Text);

    private sealed record TagModel(List<string> Tags);

    private sealed class SingleLineValidator : AbstractValidator<TextModel>
    {
        public SingleLineValidator(int max) => RuleFor(model => model.Text).SingleLineText(max);
    }

    private sealed class MultiLineValidator : AbstractValidator<TextModel>
    {
        public MultiLineValidator(int max) => RuleFor(model => model.Text).MultiLineSecretText(max);
    }

    private sealed class UrlValidator : AbstractValidator<TextModel>
    {
        public UrlValidator() => RuleFor(model => model.Text).HttpUrl();
    }

    private sealed class TagsValidator : AbstractValidator<TagModel>
    {
        public TagsValidator() => RuleFor(model => model.Tags).TagList();
    }
}
