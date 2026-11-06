// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvSliderTests : MudTestContext
{
    private const string FieldId = "generator-length";

    private IRenderedComponent<SvSlider> RenderSlider(Action<ComponentParameterCollectionBuilder<SvSlider>>? configure = null) =>
        Render<SvSlider>(parameters =>
        {
            parameters
                .Add(slider => slider.Id, FieldId)
                .Add(slider => slider.Label, "Length")
                .Add(slider => slider.Min, 8)
                .Add(slider => slider.Max, 128)
                .Add(slider => slider.Value, 20);
            configure?.Invoke(parameters);
        });

    [Fact]
    public void Render_PutsIdOnTheRealRangeInput()
    {
        var cut = RenderSlider();

        var input = Assert.Single(cut.FindAll($"#{FieldId}"));
        Assert.Equal("input", input.TagName.ToLowerInvariant());
        Assert.Equal("range", input.GetAttribute("type"));
        Assert.Equal("8", input.GetAttribute("min"));
        Assert.Equal("128", input.GetAttribute("max"));
        Assert.Equal("20", input.GetAttribute("value"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    [InlineData("1abc")]
    public void InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => RenderSlider(parameters => parameters.Add(slider => slider.Id, id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankLabel_Throws(string label)
    {
        ThrownExceptions.ArgumentFrom(() => RenderSlider(parameters => parameters.Add(slider => slider.Label, label)));
    }
}
