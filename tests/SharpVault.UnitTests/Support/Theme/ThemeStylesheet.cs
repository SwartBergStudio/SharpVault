// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support.Theme;

internal static class ThemeStylesheet
{
    public const string RelativePath = "src/SharpVault.UI.Shared/wwwroot/css/app.css";

    public static string Path { get; } = UiSourceFiles.Absolute(RelativePath);

    public static string Load() => File.ReadAllText(Path);
}
