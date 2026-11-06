// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using SharpVault.UI.Shared.Components.Icons;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvIconsTests
{
    private static List<(string Name, string Value)> Constants() =>
        typeof(SvIcons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!))
            .ToList();

    [Fact]
    public void Constants_Always_AreInlineSvgMarkup()
    {
        Assert.All(Constants(), constant =>
        {
            Assert.StartsWith("<", constant.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("http", constant.Value, StringComparison.OrdinalIgnoreCase);
        });
    }
}
