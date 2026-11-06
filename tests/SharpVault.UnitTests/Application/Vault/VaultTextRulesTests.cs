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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData(" ")]
    public void SingleLineText_WhitespaceOnly_Fails(string value)
    {
        var result = SingleLine.Validate(new TextModel(value));

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
    [InlineData("a\u0007b")]
    [InlineData("a\u001bb")]
    [InlineData("a\u007fb")]
    [InlineData("a\u0085b")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\rb")]
    public void SingleLineText_ControlCharacter_Fails(string value)
    {
        var result = SingleLine.Validate(new TextModel(value));

        Assert.Contains(VaultMessages.InvalidCharacters, Messages(result));
    }

    [Theory]
    [InlineData('‪')]
    [InlineData('‫')]
    [InlineData('‬')]
    [InlineData('‭')]
    [InlineData('‮')]
    [InlineData('⁦')]
    [InlineData('⁧')]
    [InlineData('⁨')]
    [InlineData('⁩')]
    public void SingleLineText_BidiControl_Fails(char bidi)
    {
        var result = SingleLine.Validate(new TextModel("a" + bidi + "b"));

        Assert.Contains(VaultMessages.InvalidCharacters, Messages(result));
    }

    [Fact]
    public void SingleLineText_LoneSurrogate_Fails()
    {
        string[] samples = ["a\ud800b", "a\udc00b", "\udc00\ud800", "ab\ud83d"];

        foreach (var value in samples)
        {
            var result = SingleLine.Validate(new TextModel(value));

            Assert.Contains(VaultMessages.NotWellFormedText, Messages(result));
        }
    }

    [Fact]
    public void SingleLineText_EmojiSurrogatePair_Passes()
    {
        Assert.True(SingleLine.Validate(new TextModel("a\U0001F600b")).IsValid);
    }

    [Fact]
    public void SingleLineText_OrdinaryUnicode_Passes()
    {
        Assert.True(SingleLine.Validate(new TextModel("Zoë 名前")).IsValid);
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
    [InlineData("https://example.test/")]
    [InlineData("http://example.test/path?x=1#y")]
    [InlineData("HTTPS://EXAMPLE.TEST")]
    public void HttpUrl_HttpsAndHttp_Pass(string value)
    {
        Assert.True(Url.Validate(new TextModel(value)).IsValid);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.test/")]
    [InlineData("/relative/path")]
    [InlineData("example.test")]
    [InlineData("//example.test/")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("")]
    public void HttpUrl_JavascriptDataFileFtpRelative_Fail(string value)
    {
        var result = Url.Validate(new TextModel(value));

        Assert.Contains(VaultMessages.InvalidUrl, Messages(result));
    }

    [Theory]
    [InlineData("https://example.test/a b")]
    [InlineData(" https://example.test/")]
    [InlineData("https://example.test/ ")]
    [InlineData("https://example.test/\n")]
    [InlineData("https://example.test/\u0000")]
    [InlineData("https://exa\tmple.test/")]
    public void HttpUrl_WithWhitespace_Fails(string value)
    {
        var result = Url.Validate(new TextModel(value));

        Assert.Contains(VaultMessages.InvalidUrl, Messages(result));
    }

    [Theory]
    [InlineData("‪")]
    [InlineData("‫")]
    [InlineData("‬")]
    [InlineData("‭")]
    [InlineData("‮")]
    [InlineData("⁦")]
    [InlineData("⁧")]
    [InlineData("⁨")]
    [InlineData("⁩")]
    public void HttpUrl_BidiControl_Fails(string bidi)
    {
        var result = Url.Validate(new TextModel("https://example.test/a" + bidi + "b"));

        Assert.Contains(VaultMessages.InvalidUrl, Messages(result));
    }

    [Theory]
    [InlineData("​")]
    [InlineData("‌")]
    [InlineData("‍")]
    [InlineData("‎")]
    [InlineData("⁠")]
    [InlineData("­")]
    [InlineData("﻿")]
    [InlineData("󠁁")]
    public void HttpUrl_FormatCharacter_Fails(string formatCharacter)
    {
        var result = Url.Validate(new TextModel("https://example.test/a" + formatCharacter + "b"));

        Assert.Contains(VaultMessages.InvalidUrl, Messages(result));
    }

    [Theory]
    [InlineData("‍")]
    [InlineData("​")]
    public void SingleLineText_FormatCharacter_StillPassesForNames(string formatCharacter)
    {
        Assert.True(SingleLine.Validate(new TextModel("name" + formatCharacter + "x")).IsValid);
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

    [Fact]
    public void TagList_DuplicateCaseInsensitive_Fails()
    {
        Assert.Contains(VaultMessages.DuplicateTag, Messages(Tags.Validate(new TagModel(["Prod", "prod"]))));
    }

    [Fact]
    public void TagList_DuplicateAfterTrim_Fails()
    {
        Assert.Contains(VaultMessages.DuplicateTag, Messages(Tags.Validate(new TagModel(["prod", "  PROD "]))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TagList_EmptyTag_Fails(string tag)
    {
        Assert.Contains(VaultMessages.FieldRequired, Messages(Tags.Validate(new TagModel(["ok", tag]))));
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

    [Fact]
    public void Rules_NullBuilder_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => VaultTextRules.SingleLineText<TextModel, string?>(null!, Max));
        Assert.Throws<ArgumentNullException>(() => VaultTextRules.MultiLineSecretText<TextModel, string?>(null!, Max));
        Assert.Throws<ArgumentNullException>(() => VaultTextRules.HttpUrl<TextModel, string?>(null!));
        Assert.Throws<ArgumentNullException>(() => VaultTextRules.TagList<TagModel, List<string>>(null!));
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
