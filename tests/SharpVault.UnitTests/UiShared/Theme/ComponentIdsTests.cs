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
    [InlineData(" ")]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("trailing\n")]
    [InlineData("<script>")]
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

    [Fact]
    public void ComponentIds_ValidValue_IsReturned()
    {
        Assert.Equal("secret-value", ComponentIds.EnsureValid("secret-value", ParameterName));
    }

    [Fact]
    public void ComponentIds_LowercaseGuidInDFormat_IsAccepted()
    {
        var id = $"folder-{Guid.NewGuid():D}";

        Assert.Equal(id, ComponentIds.EnsureValid(id, ParameterName));
    }
}
