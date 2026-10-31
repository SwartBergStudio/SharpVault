// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Components;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class ComponentIdsTests
{
    private const string ParameterName = "candidate";

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("1start")]
    [InlineData("a/b")]
    [InlineData("a_b")]
    [InlineData("-leading")]
    [InlineData("trailing\n")]
    [InlineData("quote\"x")]
    [InlineData("<script>")]
    [InlineData("folder-node-3F2504E0-4F89-11D3-9A0C-0305E82C3301")]
    public void ComponentIds_InvalidValue_Throws(string? value)
    {
        var exception = ThrownExceptions.ArgumentFrom(() => ComponentIds.EnsureValid(value, ParameterName));

        Assert.Equal(ParameterName, exception.ParamName);
        Assert.Contains("must be lowercase kebab-case", exception.Message, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(value))
        {
            Assert.DoesNotContain(value.Trim(), exception.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("secret-value")]
    [InlineData("folder-node-3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("a")]
    [InlineData("a1-b2")]
    public void ComponentIds_ValidValue_IsReturned(string value)
    {
        var result = ComponentIds.EnsureValid(value, ParameterName);

        Assert.Equal(value, result);
    }

    [Fact]
    public void ComponentIds_LowercaseGuidInDFormat_IsAccepted()
    {
        var id = $"folder-{Guid.NewGuid():D}";

        Assert.Equal(id, ComponentIds.EnsureValid(id, ParameterName));
    }
}
