// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.IO.Compression;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class ThemeBudgetTests
{
    private const int StylesheetRawBudget = 40960;
    private const int StylesheetGzipBudget = 10240;
    private const int IsolatedStylesBudget = 12288;
    private const int FontEachBudget = 100000;
    private const int FontTotalBudget = 240000;
    private const int FirstViewFontBudget = 100000;
    private const string FirstViewFont = "archivo-latin-wdth-normal.woff2";

    [Fact]
    public void ThemeStylesheet_RawSize_IsWithinBudget()
    {
        var size = new FileInfo(ThemeStylesheet.Path).Length;

        Assert.True(size <= StylesheetRawBudget, $"app.css is {size} bytes, budget {StylesheetRawBudget}.");
    }

    [Fact]
    public void ThemeStylesheet_GzipSize_IsWithinBudget()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            var bytes = File.ReadAllBytes(ThemeStylesheet.Path);
            gzip.Write(bytes, 0, bytes.Length);
        }

        Assert.True(
            output.Length <= StylesheetGzipBudget,
            $"app.css compresses to {output.Length} bytes, budget {StylesheetGzipBudget}.");
    }

    [Fact]
    public void UiSharedIsolatedStyles_TotalSize_IsWithinBudget()
    {
        var total = UiSourceFiles.UiSharedWithExtension(".razor.css").Sum(file => new FileInfo(file.FullPath).Length);

        Assert.True(total <= IsolatedStylesBudget, $"Isolated styles total {total} bytes, budget {IsolatedStylesBudget}.");
    }

    [Fact]
    public void FontFiles_EachWoff2_IsWithinBudget()
    {
        foreach (var asset in PinnedThemeAssets.Woff2Files)
        {
            var size = new FileInfo(PinnedThemeAssets.PathOf(asset)).Length;
            Assert.True(size <= FontEachBudget, $"{asset.FileName} is {size} bytes, budget {FontEachBudget}.");
        }
    }

    [Fact]
    public void FontFiles_TotalSize_IsWithinBudget()
    {
        var total = PinnedThemeAssets.Woff2Files.Sum(asset => new FileInfo(PinnedThemeAssets.PathOf(asset)).Length);

        Assert.True(total <= FontTotalBudget, $"Fonts total {total} bytes, budget {FontTotalBudget}.");
    }

    [Fact]
    public void FontFiles_FirstViewLatinSet_IsWithinBudget()
    {
        var asset = PinnedThemeAssets.Woff2Files.Single(file => file.FileName == FirstViewFont);

        var size = new FileInfo(PinnedThemeAssets.PathOf(asset)).Length;

        Assert.True(size <= FirstViewFontBudget, $"{FirstViewFont} is {size} bytes, budget {FirstViewFontBudget}.");
    }
}
