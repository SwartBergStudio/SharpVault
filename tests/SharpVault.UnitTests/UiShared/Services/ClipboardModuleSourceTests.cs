// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UI.Shared.Services.Clipboard;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Services;

[Trait("Category", "Security")]
public sealed partial class ClipboardModuleSourceTests
{
    private static readonly string ModuleFile = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared", "wwwroot", "js", "clipboard.js");

    private static readonly string[] BooleanArguments = ["true", "false", "text !== \"\""];

    private static string Source => File.ReadAllText(ModuleFile);

    [Fact]
    public void ClipboardModule_Source_UsesOnlyWriteText()
    {
        var source = Source;

        Assert.Contains("navigator.clipboard.writeText(", source, StringComparison.Ordinal);
        var members = ClipboardMember().Matches(source).Select(match => match.Groups["member"].Value).Distinct().ToList();
        Assert.Equal(["writeText"], members);
        foreach (var forbidden in new[] { "readText", ".read(", "ClipboardItem", "eval", "innerHTML", "outerHTML", "Function(", "document.", "localStorage", "indexedDB", "cookie", "fetch", "XMLHttpRequest", "import(" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClipboardModule_Exports_AreExactlyTheFunctionsTheBrowserClipboardCalls()
    {
        var exports = ExportedFunction().Matches(Source).Select(match => match.Groups["name"].Value).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["clear", "writeText"], exports);
    }

    [Fact]
    public void ClipboardModule_Path_MatchesTheFileThatBrowserClipboardImports()
    {
        const string prefix = "./_content/SharpVault.UI.Shared/";

        Assert.StartsWith(prefix, BrowserClipboard.ModulePath, StringComparison.Ordinal);
        var relative = BrowserClipboard.ModulePath[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
        Assert.Equal(Path.GetFullPath(ModuleFile), Path.GetFullPath(Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared", "wwwroot", relative)));
    }

    [Fact]
    public void ClipboardModule_Clear_WritesAnEmptyString()
    {
        var clear = Source[Source.IndexOf("export async function clear", StringComparison.Ordinal)..];

        Assert.Contains("writeText(\"\")", clear, StringComparison.Ordinal);
    }

    [Fact]
    public void ClipboardModule_SessionStorage_HoldsOnlyTheBooleanPendingFlag()
    {
        var source = Source;

        Assert.Contains("const storedPendingKey = \"sharpvault.secretCopyPending\";", source, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(source, @"storedPendingKey\s*=", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
        var uses = StorageUse().Matches(source).ToList();
        Assert.Equal(CountOf(source, "sessionStorage"), uses.Count);
        Assert.Equal(3, uses.Count);
        Assert.Equal(["getItem", "removeItem", "setItem"], uses.Select(use => use.Groups["member"].Value).Order(StringComparer.Ordinal).ToList());
        foreach (var use in uses)
        {
            var arguments = use.Groups["arguments"].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
            var expected = use.Groups["member"].Value == "setItem" ? "storedPendingKey,\"1\"" : "storedPendingKey";
            Assert.Equal(expected, arguments);
        }
    }

    [Fact]
    public void ClipboardModule_PendingFlag_IsOnlyEverSetFromABooleanExpression()
    {
        var source = Source;

        var calls = SetPendingCall().Matches(source).Select(match => match.Groups["argument"].Value.Trim()).ToList();

        Assert.Contains("function setPending(value)", source, StringComparison.Ordinal);
        Assert.NotEmpty(calls);
        Assert.All(calls, argument => Assert.Contains(argument, BooleanArguments));
    }

    private static int CountOf(string source, string value)
    {
        var count = 0;
        for (var index = source.IndexOf(value, StringComparison.Ordinal); index >= 0; index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [GeneratedRegex(@"sessionStorage\.(?<member>[A-Za-z]+)\((?<arguments>[^)]*)\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex StorageUse();

    [GeneratedRegex(@"(?<!function )setPending\((?<argument>[^)]*)\)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex SetPendingCall();

    [GeneratedRegex("navigator\\.clipboard\\.(?<member>[A-Za-z]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ClipboardMember();

    [GeneratedRegex("export\\s+(?:async\\s+)?function\\s+(?<name>[A-Za-z]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ExportedFunction();
}
