// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvCheckboxTests : MudTestContext
{
    private const string FieldId = "secret-change-value";

    private IRenderedComponent<SvCheckbox> RenderCheckbox(Action<ComponentParameterCollectionBuilder<SvCheckbox>>? configure = null) =>
        Render<SvCheckbox>(parameters =>
        {
            parameters
                .Add(checkbox => checkbox.Id, FieldId)
                .Add(checkbox => checkbox.Label, "Change the value");
            configure?.Invoke(parameters);
        });

    [Fact]
    public void Render_PutsIdOnTheRealInput()
    {
        var cut = RenderCheckbox();

        var matches = cut.FindAll($"#{FieldId}");
        var input = Assert.Single(matches);
        Assert.Equal("input", input.TagName.ToLowerInvariant());
        Assert.Equal("checkbox", input.GetAttribute("type"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Change_RaisesValueChanged(bool value)
    {
        bool? received = null;
        var cut = RenderCheckbox(parameters => parameters
            .Add(checkbox => checkbox.Value, !value)
            .Add(checkbox => checkbox.ValueChanged, changed => received = changed));

        cut.Find($"#{FieldId}").Change(value);

        Assert.Equal(value, received);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    [InlineData("1abc")]
    public void InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvCheckbox>(parameters => parameters
            .Add(checkbox => checkbox.Id, id)
            .Add(checkbox => checkbox.Label, "Value")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankLabel_Throws(string label)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvCheckbox>(parameters => parameters
            .Add(checkbox => checkbox.Id, FieldId)
            .Add(checkbox => checkbox.Label, label)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Change_ParentKeepsItsValue_InputShowsTheParentValue(bool parentValue)
    {
        var cut = RenderCheckbox(parameters => parameters
            .Add(checkbox => checkbox.Value, parentValue)
            .Add(checkbox => checkbox.ValueChanged, _ => { }));

        cut.Find($"#{FieldId}").Change(!parentValue);

        cut.WaitForAssertion(() => Assert.Equal(parentValue, cut.Find($"#{FieldId}").HasAttribute("checked")));
    }
}
