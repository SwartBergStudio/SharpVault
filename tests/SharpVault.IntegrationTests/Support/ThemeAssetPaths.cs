// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Support;

public static class ThemeAssetPaths
{
    public const string ContentRoot = "/_content/SharpVault.UI.Shared";
    public const string Stylesheet = ContentRoot + "/css/app.css";
    public const string Favicon = ContentRoot + "/brand/favicon.svg";
    public const string FirstViewFont = ContentRoot + "/fonts/archivo-latin-wdth-normal.woff2";
    public const string FirstViewFontSha256 = "e3a28eade21a900c7155a247757f4b2834c07bb7ef07ad7efa55cebaac1e8f5e";

    public static IReadOnlyList<string> Fonts { get; } =
    [
        ContentRoot + "/fonts/archivo-latin-wdth-normal.woff2",
        ContentRoot + "/fonts/archivo-latin-ext-wdth-normal.woff2",
        ContentRoot + "/fonts/jetbrains-mono-latin-wght-normal.woff2",
        ContentRoot + "/fonts/jetbrains-mono-latin-ext-wght-normal.woff2",
    ];

    public static IReadOnlyList<string> Licences { get; } =
    [
        ContentRoot + "/fonts/OFL-Archivo.txt",
        ContentRoot + "/fonts/OFL-JetBrainsMono.txt",
    ];
}
