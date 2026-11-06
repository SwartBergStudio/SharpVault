// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

[Trait("Category", "Security")]
public sealed class SecretFieldTests : MudTestContext
{
    private const string Prefix = "secret-value";
    private const int LongContentLength = 128;
    private readonly Fixture _fixture = new();

    [Fact]
    public void SecretField_RevealedNotSet_DefaultsToHidden()
    {
        var cut = Render<SecretField>(parameters => parameters.Add(field => field.IdPrefix, Prefix));

        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
        Assert.Equal("Hidden", cut.Find($"#{Prefix}-state span").TextContent.Trim());
    }

    [Fact]
    public void SecretField_Hidden_DoesNotRenderContent()
    {
        var marker = $"SV-SECRET-{_fixture.Create<Guid>():N}";

        var cut = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, false)
            .AddChildContent($"<b id=\"inner\">{marker}</b>"));

        Assert.DoesNotContain(marker, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#inner"));
    }

    [Fact]
    public void SecretField_Revealed_ValueIsMarkedSensitiveOnlyWhileRevealed()
    {
        var revealed = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, true));
        var hidden = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, false));

        Assert.Equal("true", revealed.Find($"#{Prefix}").GetAttribute("data-sv-sensitive"));
        Assert.Null(hidden.Find($"#{Prefix}").GetAttribute("data-sv-sensitive"));
    }

    [Fact]
    public void SecretField_HiddenWithTextRenderFragment_DoesNotRenderContent()
    {
        var marker = $"SV-SECRET-{_fixture.Create<Guid>():N}";

        var cut = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.ChildContent, builder => builder.AddContent(0, marker)));

        Assert.DoesNotContain(marker, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretField_HiddenWithShortOrLongContent_RendersIdenticalMask()
    {
        var shortContent = Render<SecretField>(parameters => parameters.Add(field => field.IdPrefix, Prefix).AddChildContent("a"));
        var longContent = Render<SecretField>(parameters => parameters.Add(field => field.IdPrefix, Prefix).AddChildContent(new string('x', LongContentLength)));
        var noContent = Render<SecretField>(parameters => parameters.Add(field => field.IdPrefix, Prefix));

        var shortMarkup = shortContent.Find($"#{Prefix}").OuterHtml;
        Assert.Equal(shortMarkup, longContent.Find($"#{Prefix}").OuterHtml);
        Assert.Equal(shortMarkup, noContent.Find($"#{Prefix}").OuterHtml);
        Assert.Contains(SecretField.MaskText, shortContent.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretField_Revealed_RendersVisibleLabelAndContent()
    {
        var marker = $"SV-SECRET-{_fixture.Create<Guid>():N}";

        var cut = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, true)
            .AddChildContent(marker));

        Assert.Equal("revealed", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
        Assert.Equal("Visible", cut.Find($"#{Prefix}-state span").TextContent.Trim());
        Assert.Equal(marker, cut.Find($"#{Prefix}").TextContent.Trim());
        Assert.DoesNotContain(SecretField.MaskText, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretField_Revealed_PreservesLeadingTrailingAndRepeatedSpaces()
    {
        const string value = "  pass  word  ";

        var cut = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, true)
            .Add(field => field.ChildContent, builder => builder.AddContent(0, value)));

        Assert.Equal(value, cut.Find($"#{Prefix}").TextContent);
    }

    [Fact]
    public void SecretField_RevealedValueWithMarkup_IsEncoded()
    {
        const string value = "<img src=x onerror=alert(1)>";

        var cut = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, true)
            .Add(field => field.ChildContent, builder => builder.AddContent(0, value)));

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal(value, cut.Find($"#{Prefix}").TextContent);
    }

    [Fact]
    public void SecretField_RevealedThenHidden_RemovesContentFromMarkup()
    {
        var marker = $"SV-SECRET-{_fixture.Create<Guid>():N}";
        var cut = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, true)
            .AddChildContent(marker));
        Assert.Contains(marker, cut.Markup, StringComparison.Ordinal);

        cut.Render(parameters => parameters.Add(field => field.Revealed, false));

        Assert.DoesNotContain(marker, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("hidden", cut.Find($"#{Prefix}-frame").GetAttribute("data-state"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SecretField_AnyState_ValueOptsOutOfTranslation(bool revealed)
    {
        var cut = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, revealed)
            .AddChildContent("value"));

        var value = cut.Find($"#{Prefix}");
        Assert.Equal("no", value.GetAttribute("translate"));
        Assert.Contains("notranslate", value.ClassList);
    }

    [Fact]
    public void SecretField_Ids_DerivedFromIdPrefix()
    {
        var cut = Render<SecretField>(parameters => parameters.Add(field => field.IdPrefix, Prefix));

        Assert.NotNull(cut.Find($"div#{Prefix}-frame"));
        Assert.NotNull(cut.Find($"span#{Prefix}-state"));
        Assert.NotNull(cut.Find($"span#{Prefix}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void SecretField_InvalidPrefix_Throws(string? prefix)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SecretField>(parameters => parameters.Add(field => field.IdPrefix, prefix!)));
    }
}
