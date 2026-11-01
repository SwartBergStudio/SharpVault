// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AngleSharp.Dom;
using Bunit;
using SharpVault.UI.Shared.Components.Icons;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class IconTests : BunitContext
{
    private static readonly string[] AllowedShapes = ["path", "rect", "circle", "line", "polyline", "polygon", "ellipse", "title"];

    public static TheoryData<IconName> AllNames()
    {
        var data = new TheoryData<IconName>();
        foreach (var name in Enum.GetValues<IconName>())
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void Icon_Names_CountThirtyOne()
    {
        Assert.Equal(31, Enum.GetValues<IconName>().Length);
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Icon_EveryName_RendersSvgWithChildren(IconName name)
    {
        var cut = Render<Icon>(parameters => parameters.Add(icon => icon.Name, name));

        var svg = cut.Find("svg.sv-icon");
        Assert.Equal("0 0 24 24", svg.GetAttribute("viewBox"));
        Assert.NotEmpty(svg.Children);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(AllNames))]
    public void Icon_EveryName_RendersOnlyBasicShapes(IconName name)
    {
        var cut = Render<Icon>(parameters => parameters
            .Add(icon => icon.Name, name)
            .Add(icon => icon.Label, "Label"));

        var svg = cut.Find("svg");
        foreach (var element in svg.QuerySelectorAll("*"))
        {
            Assert.Contains(element.LocalName, AllowedShapes);
            Assert.All(element.Attributes, attribute => AssertSafeAttribute(attribute, element));
        }

        Assert.All(svg.Attributes, attribute => AssertSafeAttribute(attribute, svg));
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Icon_EveryName_UsesCurrentColour(IconName name)
    {
        var cut = Render<Icon>(parameters => parameters.Add(icon => icon.Name, name));

        var svg = cut.Find("svg");
        Assert.Equal("currentColor", svg.GetAttribute("stroke"));
        Assert.Equal("none", svg.GetAttribute("fill"));
    }

    [Fact]
    public void Icon_AllNames_RenderDistinctMarkup()
    {
        var markups = Enum.GetValues<IconName>()
            .Select(name => Render<Icon>(parameters => parameters.Add(icon => icon.Name, name)).Find("svg").InnerHtml)
            .ToList();

        Assert.Equal(markups.Count, markups.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Icon_WithoutLabel_IsHiddenFromAssistiveTechnology()
    {
        var cut = Render<Icon>(parameters => parameters.Add(icon => icon.Name, IconName.Lock));

        var svg = cut.Find("svg");
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.Equal("false", svg.GetAttribute("focusable"));
        Assert.False(svg.HasAttribute("role"));
        Assert.False(svg.HasAttribute("aria-label"));
        Assert.Empty(cut.FindAll("title"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Icon_BlankLabel_IsTreatedAsDecorative(string label)
    {
        var cut = Render<Icon>(parameters => parameters
            .Add(icon => icon.Name, IconName.Lock)
            .Add(icon => icon.Label, label));

        var svg = cut.Find("svg");
        Assert.Equal("true", svg.GetAttribute("aria-hidden"));
        Assert.False(svg.HasAttribute("role"));
        Assert.Empty(cut.FindAll("title"));
    }

    [Fact]
    public void Icon_WithLabel_HasImageRoleAndTitle()
    {
        var cut = Render<Icon>(parameters => parameters
            .Add(icon => icon.Name, IconName.ShieldCheck)
            .Add(icon => icon.Label, "Account is active"));

        var svg = cut.Find("svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Equal("Account is active", svg.GetAttribute("aria-label"));
        Assert.False(svg.HasAttribute("aria-hidden"));
        var title = svg.FirstElementChild;
        Assert.NotNull(title);
        Assert.Equal("title", title.LocalName);
        Assert.Equal("Account is active", title.TextContent);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Icon_LabelWithMarkup_IsEncoded()
    {
        const string label = "<script>alert(1)</script>\"onload=\"x";

        var cut = Render<Icon>(parameters => parameters
            .Add(icon => icon.Name, IconName.Info)
            .Add(icon => icon.Label, label));

        Assert.Empty(cut.FindAll("script"));
        Assert.Single(cut.FindAll("title"));
        Assert.Equal(label, cut.Find("title").TextContent);
        Assert.Equal(label, cut.Find("svg").GetAttribute("aria-label"));
    }

    [Fact]
    public void Icon_WithCssClass_AddsClassNextToIconClass()
    {
        var cut = Render<Icon>(parameters => parameters
            .Add(icon => icon.Name, IconName.Vault)
            .Add(icon => icon.CssClass, "sv-icon--xl"));

        var classes = cut.Find("svg").ClassList;
        Assert.Contains("sv-icon", classes);
        Assert.Contains("sv-icon--xl", classes);
    }

    [Fact]
    public void Icon_Rendered_HasNoSizeStyleOrReferenceAttributes()
    {
        var cut = Render<Icon>(parameters => parameters.Add(icon => icon.Name, IconName.Vault));

        var svg = cut.Find("svg");
        Assert.False(svg.HasAttribute("width"));
        Assert.False(svg.HasAttribute("height"));
        Assert.False(svg.HasAttribute("style"));
        Assert.Empty(cut.FindAll("use"));
    }

    private static void AssertSafeAttribute(IAttr attribute, IElement owner)
    {
        var name = attribute.Name.ToLowerInvariant();
        Assert.False(name is "style" or "href" or "xlink:href", $"{owner.LocalName} has forbidden attribute {attribute.Name}.");
        Assert.False(name.StartsWith("on", StringComparison.Ordinal), $"{owner.LocalName} has handler attribute {attribute.Name}.");
    }
}
