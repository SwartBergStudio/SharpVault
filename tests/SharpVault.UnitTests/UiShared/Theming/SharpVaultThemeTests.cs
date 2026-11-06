// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using MudBlazor;
using MudBlazor.Utilities;
using SharpVault.UI.Shared.Theming;

namespace SharpVault.UnitTests.UiShared.Theming;

public sealed class SharpVaultThemeTests
{
    [Fact]
    public void Theme_Always_HasNoExternalUrls()
    {
        var text = string.Join('\n', new[] { SharpVaultTheme.Instance.PaletteDark, (object)SharpVaultTheme.Instance.PaletteLight, SharpVaultTheme.Instance.Typography, SharpVaultTheme.Instance.LayoutProperties }
            .SelectMany(Strings));

        Assert.NotEmpty(text);
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("//", text, StringComparison.Ordinal);
    }

    private static IEnumerable<string> Strings(object root)
    {
        foreach (var property in root.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            var value = property.GetValue(root);
            switch (value)
            {
                case string text:
                    yield return text;
                    break;
                case MudColor colour:
                    yield return colour.ToString(MudColorOutputFormats.Hex);
                    break;
                case string[] many:
                    foreach (var item in many)
                    {
                        yield return item;
                    }

                    break;
                case BaseTypography typography:
                    foreach (var item in Strings(typography))
                    {
                        yield return item;
                    }

                    break;
            }
        }
    }
}
