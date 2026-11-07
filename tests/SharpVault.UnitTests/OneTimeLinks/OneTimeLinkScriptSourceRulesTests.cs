// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed partial class OneTimeLinkScriptSourceRulesTests
{
    private const string LinkId = "00000000-0000-0000-0000-00000000c0de";
    private const string BaseUri = "https://sharpvault.example/";

    private static readonly string ScriptRoot = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared", "wwwroot", "js");
    private static readonly string CreateScriptPath = Path.Combine(ScriptRoot, "one-time-link-create.js");
    private static readonly string OpenScriptPath = Path.Combine(ScriptRoot, "one-time-link-open.js");
    private static readonly string CryptoScriptPath = Path.Combine(ScriptRoot, "one-time-link-crypto.js");
    private static readonly string CreateHarnessPath = Path.Combine(RepositoryRoot.Path, "tests", "SharpVault.UnitTests", "UiShared", "OneTimeLinks", "one-time-link-create.harness.mjs");

    [Fact]
    public void OpenScript_NeverUsesHtmlSinksOrConsole()
    {
        var source = File.ReadAllText(OpenScriptPath);

        Assert.DoesNotMatch(HtmlSinkOrConsole(), source);
    }

    [Fact]
    public void CreateScript_NeverUsesHtmlSinksOrConsole()
    {
        var source = File.ReadAllText(CreateScriptPath);

        Assert.DoesNotMatch(HtmlSinkOrConsole(), source);
    }

    [Fact]
    public void CreateScript_NoExportedFunctionReturnsTheFragment()
    {
        var returned = RunCreateHarness();

        Assert.NotEmpty(returned.Fragment);
        Assert.DoesNotContain(returned.Fragment, returned.ReturnedJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateScript_SealKeepsThePassphraseInTheInputUntilPublish()
    {
        var returned = RunCreateHarness("correct horse battery");

        Assert.Equal("correct horse battery", returned.PassphraseAfterSeal);
    }

    [Fact]
    public void CreateScript_PublishClearsThePassphraseInput()
    {
        var returned = RunCreateHarness("correct horse battery");

        Assert.True(returned.PassphraseCleared);
    }

    [Fact]
    public void CryptoScript_UsesNoDomApi()
    {
        var source = File.ReadAllText(CryptoScriptPath);

        Assert.DoesNotMatch(DomApi(), source);
    }

    private static CreateRun RunCreateHarness(string passphrase = "")
    {
        var scenario = JsonSerializer.Serialize(new { snapshot = "{\"name\":\"fragment-check\"}", passphrase, baseUri = BaseUri, linkId = LinkId });
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(CreateHarnessPath);
        startInfo.ArgumentList.Add(CreateScriptPath);
        startInfo.ArgumentList.Add(scenario);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("node could not be started.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, error);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var fragment = root.GetProperty("fragment").GetString() ?? string.Empty;
        var returned = root.GetProperty("returned").GetRawText();
        var passphraseAfterSeal = root.GetProperty("passphraseAfterSeal").GetString() ?? string.Empty;
        var passphraseCleared = root.GetProperty("passphraseCleared").GetBoolean();
        return new CreateRun(fragment, returned, passphraseAfterSeal, passphraseCleared);
    }

    [GeneratedRegex(@"\b(innerHTML|outerHTML|insertAdjacentHTML|document\.write|eval|Function|console)\b|new\s+Function")]
    private static partial Regex HtmlSinkOrConsole();

    [GeneratedRegex(@"\b(document|window|localStorage|sessionStorage|navigator|location|history)\b")]
    private static partial Regex DomApi();

    private sealed record CreateRun(string Fragment, string ReturnedJson, string PassphraseAfterSeal, bool PassphraseCleared);
}
