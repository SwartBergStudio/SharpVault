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

    [Fact]
    public void SecretField_Revealed_ValueOptsOutOfTranslation()
    {
        var cut = Render<SecretField>(parameters => parameters
            .Add(field => field.IdPrefix, Prefix)
            .Add(field => field.Revealed, true)
            .AddChildContent("value"));

        var value = cut.Find($"#{Prefix}");
        Assert.Equal("no", value.GetAttribute("translate"));
        Assert.Contains("notranslate", value.ClassList);
    }
}
