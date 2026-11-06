// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvSelectTests : MudTestContext
{
    private const string FieldId = "secret-type";

    private static readonly SvSelectOption[] DefaultOptions =
    [
        new("login", "Login"),
        new("note", "Secure note"),
        new("card", "Card"),
    ];

    private IRenderedComponent<SvSelect> RenderSelect(Action<ComponentParameterCollectionBuilder<SvSelect>>? configure = null, IReadOnlyList<SvSelectOption>? options = null) =>
        Render<SvSelect>(parameters =>
        {
            parameters
                .Add(select => select.Id, FieldId)
                .Add(select => select.Label, "Type")
                .Add(select => select.Options, options ?? DefaultOptions);
            configure?.Invoke(parameters);
        });

    [Fact]
    public void Render_LabelPointsAtTheSelect()
    {
        var cut = RenderSelect();

        var select = Assert.Single(cut.FindAll($"select#{FieldId}"));
        var label = cut.Find("label");
        Assert.Equal(FieldId, label.GetAttribute("for"));
        Assert.Equal("Type", label.TextContent);
        Assert.Equal(3, select.QuerySelectorAll("option").Length);
    }

    [Fact]
    public void Change_RaisesValueChanged()
    {
        string? received = null;
        var cut = RenderSelect(parameters => parameters.Add(select => select.ValueChanged, value => received = value));

        cut.Find($"#{FieldId}").Change("card");

        Assert.Equal("card", received);
    }

    [Fact]
    public void Value_SelectsTheOption()
    {
        var cut = RenderSelect(parameters => parameters.Add(select => select.Value, "note"));

        var selected = cut.FindAll("option").Where(option => option.HasAttribute("selected")).ToList();
        var only = Assert.Single(selected);
        Assert.Equal("note", only.GetAttribute("value"));
    }

    [Fact]
    public void RemovingAnOptionBeforeTheSelectedOne_KeepsTheSelectedValue()
    {
        var cut = RenderSelect(parameters => parameters.Add(select => select.Value, "card"));

        cut.Render(parameters => parameters
            .Add(select => select.Options, DefaultOptions.Skip(1).ToList())
            .Add(select => select.Value, "card"));

        Assert.Equal("card", Assert.Single(cut.FindAll("option"), option => option.HasAttribute("selected")).GetAttribute("value"));
    }

    [Fact]
    public void PlaceholderFirst_RemovingAnOptionBeforeTheSelectedOne_KeepsTheSelectedValue()
    {
        var withPlaceholder = new List<SvSelectOption> { new(string.Empty, "Choose a user") };
        withPlaceholder.AddRange(DefaultOptions);
        var cut = RenderSelect(parameters => parameters.Add(select => select.Value, "note"), withPlaceholder);

        cut.Render(parameters => parameters
            .Add(select => select.Options, withPlaceholder.Where(option => option.Value is not "login").ToList())
            .Add(select => select.Value, "note"));

        Assert.Equal("note", Assert.Single(cut.FindAll("option"), option => option.HasAttribute("selected")).GetAttribute("value"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void OptionText_IsEncoded()
    {
        var cut = RenderSelect(options: [new("x", "<b onclick=\"steal()\">bold</b>"), new("\"><script>", "plain")]);

        Assert.Empty(cut.FindAll("b"));
        Assert.Empty(cut.FindAll("script"));
        Assert.Contains("&lt;b onclick", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvSelect>(parameters => parameters
            .Add(select => select.Id, id)
            .Add(select => select.Label, "Type")
            .Add(select => select.Options, DefaultOptions)));
    }

    [Fact]
    public void BlankLabel_Throws()
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvSelect>(parameters => parameters
            .Add(select => select.Id, FieldId)
            .Add(select => select.Label, " ")
            .Add(select => select.Options, DefaultOptions)));
    }

}
