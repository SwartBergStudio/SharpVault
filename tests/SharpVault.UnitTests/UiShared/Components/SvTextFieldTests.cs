// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvTextFieldTests : MudTestContext
{
    private const string FieldId = "secret-value-input";

    private static readonly string[] SecretAttributeNames = ["autocomplete", "spellcheck", "autocapitalize", "autocorrect"];

    private IRenderedComponent<SvTextField> RenderField(Action<ComponentParameterCollectionBuilder<SvTextField>>? configure = null) =>
        Render<SvTextField>(parameters =>
        {
            parameters
                .Add(field => field.Id, FieldId)
                .Add(field => field.Label, "Value");
            configure?.Invoke(parameters);
        });

    private static void AssertSecretAttributes(AngleSharp.Dom.IElement element)
    {
        Assert.Equal("new-password", element.GetAttribute("autocomplete"));
        Assert.Equal("false", element.GetAttribute("spellcheck"));
        Assert.Equal("off", element.GetAttribute("autocapitalize"));
        Assert.Equal("off", element.GetAttribute("autocorrect"));
        Assert.Equal("true", element.GetAttribute("data-sv-sensitive"));
    }

    [Fact]
    public void Render_PutsIdOnTheInput()
    {
        var cut = RenderField();

        var matches = cut.FindAll($"#{FieldId}");
        var element = Assert.Single(matches);
        Assert.Equal("input", element.TagName.ToLowerInvariant());
        Assert.Equal("text", element.GetAttribute("type"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Secret_RendersPasswordWithAllSecretAttributes()
    {
        var cut = RenderField(parameters => parameters.Add(field => field.Secret, true));

        var input = Assert.Single(cut.FindAll($"#{FieldId}"));
        Assert.Equal("input", input.TagName.ToLowerInvariant());
        Assert.Equal("password", input.GetAttribute("type"));
        AssertSecretAttributes(input);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Secret_AttributesLandOnTheInnerInputNotOnAWrapper()
    {
        var cut = RenderField(parameters => parameters.Add(field => field.Secret, true));

        var input = cut.Find($"#{FieldId}");
        foreach (var name in SecretAttributeNames)
        {
            Assert.Single(cut.FindAll($"[{name}]"));
            Assert.True(input.HasAttribute(name), name);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Secret_Revealed_RendersTextWithSecretAttributesStillSet()
    {
        var cut = RenderField(parameters => parameters
            .Add(field => field.Secret, true)
            .Add(field => field.Revealed, true));

        var input = cut.Find($"#{FieldId}");
        Assert.Equal("text", input.GetAttribute("type"));
        AssertSecretAttributes(input);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Secret_Lines_RendersTextareaWithTheIdAndSecretAttributes()
    {
        var cut = RenderField(parameters => parameters
            .Add(field => field.Secret, true)
            .Add(field => field.Lines, 4));

        var area = Assert.Single(cut.FindAll($"#{FieldId}"));
        Assert.Equal("textarea", area.TagName.ToLowerInvariant());
        AssertSecretAttributes(area);
    }

    [Fact]
    public void NotSecret_RendersNoSecretAttributes()
    {
        var cut = RenderField();

        var input = cut.Find($"#{FieldId}");
        Assert.NotEqual("new-password", input.GetAttribute("autocomplete"));
        Assert.Null(input.GetAttribute("spellcheck"));
        Assert.Null(input.GetAttribute("autocapitalize"));
        Assert.Null(input.GetAttribute("autocorrect"));
        Assert.Null(input.GetAttribute("data-sv-sensitive"));
    }

    [Fact]
    public void Lines_RendersTextareaWithTheId()
    {
        var cut = RenderField(parameters => parameters.Add(field => field.Lines, 5));

        var area = Assert.Single(cut.FindAll($"#{FieldId}"));
        Assert.Equal("textarea", area.TagName.ToLowerInvariant());
    }

    [Fact]
    public void Value_Set_IsRenderedInTheInputAndEncoded()
    {
        var cut = RenderField(parameters => parameters.Add(field => field.Value, "\"><script>x</script>"));

        Assert.Empty(cut.FindAll("script"));
        Assert.Equal("\"><script>x</script>", cut.Find($"#{FieldId}").GetAttribute("value"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    [InlineData("1abc")]
    public void InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvTextField>(parameters => parameters
            .Add(field => field.Id, id)
            .Add(field => field.Label, "Value")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankLabel_Throws(string label)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvTextField>(parameters => parameters
            .Add(field => field.Id, FieldId)
            .Add(field => field.Label, label)));
    }
}
