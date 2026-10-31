// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Icons;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

[Trait("Category", "Security")]
public sealed class SecretFrameTests : BunitContext
{
    private const string Prefix = "secret-value";
    private const int LongContentLength = 128;
    private const int BulletCount = 8;
    private const char Bullet = '\u2022';

    private readonly Fixture _fixture = new();

    [Fact]
    public void SecretFrame_RevealedNotSet_DefaultsToHidden()
    {
        var cut = Render<SecretFrame>(parameters => parameters.Add(frame => frame.IdPrefix, Prefix));

        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
        Assert.Equal("Hidden", cut.Find($"#{Prefix}-state span").TextContent.Trim());
    }

    [Fact]
    public void SecretFrame_Hidden_DoesNotRenderContent()
    {
        var marker = $"SV-SECRET-{_fixture.Create<Guid>():N}";

        var cut = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .Add(frame => frame.Revealed, false)
            .AddChildContent($"<b id=\"inner\">{marker}</b>"));

        Assert.DoesNotContain(marker, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#inner"));
    }

    [Fact]
    public void SecretFrame_HiddenWithTextRenderFragment_DoesNotRenderContent()
    {
        var marker = $"SV-SECRET-{_fixture.Create<Guid>():N}";

        var cut = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .Add(frame => frame.ChildContent, builder => builder.AddContent(0, marker)));

        Assert.DoesNotContain(marker, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretFrame_HiddenWithShortOrLongContent_RendersIdenticalMask()
    {
        var shortContent = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .AddChildContent("a"));
        var longContent = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .AddChildContent(new string('x', LongContentLength)));
        var noContent = Render<SecretFrame>(parameters => parameters.Add(frame => frame.IdPrefix, Prefix));

        var shortMarkup = shortContent.Find($"#{Prefix}").OuterHtml;
        Assert.Equal(shortMarkup, longContent.Find($"#{Prefix}").OuterHtml);
        Assert.Equal(shortMarkup, noContent.Find($"#{Prefix}").OuterHtml);
        Assert.Equal(SecretFrame.MaskText, shortContent.Find(".sv-secret__mask").TextContent);
    }

    [Fact]
    public void SecretFrame_MaskText_IsExactlyEightBullets()
    {
        Assert.Equal(new string(Bullet, BulletCount), SecretFrame.MaskText);
        Assert.Equal(BulletCount, SecretFrame.MaskText.Length);
    }

    [Fact]
    public void SecretFrame_Hidden_MaskIsHiddenFromAssistiveTechnologyAndHasTextAlternative()
    {
        var cut = Render<SecretFrame>(parameters => parameters.Add(frame => frame.IdPrefix, Prefix));

        Assert.Equal("true", cut.Find(".sv-secret__mask").GetAttribute("aria-hidden"));
        Assert.Equal("Hidden value", cut.Find(".sv-secret__value .sv-visually-hidden").TextContent);
    }

    [Fact]
    public void SecretFrame_Hidden_RendersHiddenLabelAndLockIcon()
    {
        var cut = Render<SecretFrame>(parameters => parameters.Add(frame => frame.IdPrefix, Prefix));
        var lockIcon = Render<Icon>(parameters => parameters.Add(icon => icon.Name, IconName.Lock));

        Assert.Equal("Hidden", cut.Find($"#{Prefix}-state span").TextContent.Trim());
        Assert.Equal(lockIcon.Find("svg").InnerHtml, cut.Find($"#{Prefix}-state svg").InnerHtml);
    }

    [Fact]
    public void SecretFrame_Revealed_RendersVisibleLabelEyeIconAndContent()
    {
        var marker = $"SV-SECRET-{_fixture.Create<Guid>():N}";

        var cut = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .Add(frame => frame.Revealed, true)
            .AddChildContent(marker));
        var eyeIcon = Render<Icon>(parameters => parameters.Add(icon => icon.Name, IconName.Eye));

        Assert.Equal("revealed", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
        Assert.Equal("Visible", cut.Find($"#{Prefix}-state span").TextContent.Trim());
        Assert.Equal(eyeIcon.Find("svg").InnerHtml, cut.Find($"#{Prefix}-state svg").InnerHtml);
        Assert.Equal(marker, cut.Find($"#{Prefix}").TextContent.Trim());
        Assert.Empty(cut.FindAll(".sv-secret__mask"));
    }

    [Fact]
    public void SecretFrame_Revealed_PreservesLeadingTrailingAndRepeatedSpaces()
    {
        const string value = "  pass  word  ";

        var cut = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .Add(frame => frame.Revealed, true)
            .Add(frame => frame.ChildContent, builder => builder.AddContent(0, value)));

        Assert.Equal(value, cut.Find($"#{Prefix}").TextContent);
    }

    [Fact]
    public void SecretFrame_RevealedValueWithMarkup_IsEncoded()
    {
        const string value = "<img src=x onerror=alert(1)>";

        var cut = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .Add(frame => frame.Revealed, true)
            .Add(frame => frame.ChildContent, builder => builder.AddContent(0, value)));

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal(value, cut.Find($"#{Prefix}").TextContent);
    }

    [Fact]
    public void SecretFrame_RevealedThenHidden_RemovesContentFromMarkup()
    {
        var marker = $"SV-SECRET-{_fixture.Create<Guid>():N}";
        var cut = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .Add(frame => frame.Revealed, true)
            .AddChildContent(marker));
        Assert.Contains(marker, cut.Markup, StringComparison.Ordinal);

        cut.Render(parameters => parameters.Add(frame => frame.Revealed, false));

        Assert.DoesNotContain(marker, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SecretFrame_AnyState_ValueOptsOutOfTranslation(bool revealed)
    {
        var cut = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .Add(frame => frame.Revealed, revealed)
            .AddChildContent("value"));

        var value = cut.Find($"#{Prefix}");
        Assert.Equal("no", value.GetAttribute("translate"));
        Assert.Contains("notranslate", value.ClassList);
        Assert.Contains("sv-secret__value", value.ClassList);
    }

    [Fact]
    public void SecretFrame_Ids_DerivedFromIdPrefix()
    {
        var cut = Render<SecretFrame>(parameters => parameters.Add(frame => frame.IdPrefix, Prefix));

        Assert.NotNull(cut.Find($"div#{Prefix}-frame"));
        Assert.NotNull(cut.Find($"span#{Prefix}-state"));
        Assert.NotNull(cut.Find($"span#{Prefix}"));
    }

    [Fact]
    public void SecretFrame_Actions_RenderedOnlyWhenSupplied()
    {
        var without = Render<SecretFrame>(parameters => parameters.Add(frame => frame.IdPrefix, Prefix));
        var with = Render<SecretFrame>(parameters => parameters
            .Add(frame => frame.IdPrefix, Prefix)
            .Add(frame => frame.Actions, "<button id=\"secret-reveal\" type=\"button\">Reveal</button>"));

        Assert.Empty(without.FindAll(".sv-secret__actions"));
        Assert.NotNull(with.Find(".sv-secret__actions #secret-reveal"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("a/b")]
    public void SecretFrame_InvalidIdPrefix_Throws(string? prefix)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SecretFrame>(parameters => parameters.Add(frame => frame.IdPrefix, prefix!)));
    }
}
