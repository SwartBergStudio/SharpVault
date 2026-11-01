// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support.Theme;

internal sealed record PinnedAsset(string FileName, long Size, string Sha256);

internal static class PinnedThemeAssets
{
    public const string FontDirectory = "src/SharpVault.UI.Shared/wwwroot/fonts";
    public const string FaviconPath = "src/SharpVault.UI.Shared/wwwroot/brand/favicon.svg";

    public static IReadOnlyList<PinnedAsset> Woff2Files { get; } =
    [
        new("archivo-latin-wdth-normal.woff2", 90104, "e3a28eade21a900c7155a247757f4b2834c07bb7ef07ad7efa55cebaac1e8f5e"),
        new("archivo-latin-ext-wdth-normal.woff2", 86240, "5717f37059660ca5c899bad6c48ee22c3ac55cb3c484055241689d0f905a1a86"),
        new("jetbrains-mono-latin-wght-normal.woff2", 40404, "18be452724bfdc236c074ca94a249a7f41a86752c7d04ab258ce9ed5651f6a7e"),
        new("jetbrains-mono-latin-ext-wght-normal.woff2", 15196, "79bfdab9ba467e26eea4122e6f2567e188dd8a09a8c730d501fc487c4ab99c6e"),
    ];

    public static IReadOnlyList<PinnedAsset> LicenceFiles { get; } =
    [
        new("OFL-Archivo.txt", 4503, "c3c8402eec0e31dc9a76851235b1c02a0bb821488b2db8e35bcc2ab60ca8fc2b"),
        new("OFL-JetBrainsMono.txt", 4524, "403581b69dac5cff4079205e01c6b467e56af449ecbd7247693ddb1baafa005b"),
    ];

    public static IReadOnlyList<PinnedAsset> All { get; } = [.. Woff2Files, .. LicenceFiles];

    public static string PathOf(PinnedAsset asset) =>
        UiSourceFiles.Absolute(FontDirectory + "/" + asset.FileName);
}
