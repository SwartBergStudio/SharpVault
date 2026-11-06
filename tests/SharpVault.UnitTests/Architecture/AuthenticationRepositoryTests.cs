// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

[Trait("Category", "Security")]
public sealed partial class AuthenticationRepositoryTests
{
    private const string ForbiddenKey = "UseLocalAccount";
    private const string KeyStoreFile = "LocalSigningKeyStore.cs";
    private const string ThisFile = "AuthenticationRepositoryTests.cs";
    private const string AppHostGuardFile = "AppHostModelTests.cs";

    private static readonly string[] ScannedExtensions = ["*.json", "*.cs", "*.razor", "*.md", "*.yml", "*.props", "*.csproj"];

    private static readonly string[] FileWriteApis =
    [
        "File.Write",
        "File.Create",
        "File.Move",
        "File.Open",
        "File.Append",
        "File.Copy",
        "File.Delete",
        "Directory.Create",
        "new FileStream",
        "new StreamWriter",
    ];

    private static readonly string[] CommentFreeDirectories =
    [
        "src/SharpVault.Authentication",
    ];

    private static readonly string[] CommentFreeFiles =
    [
        "src/SharpVault.Api/Security/LocalAccountSeeder.cs",
        "src/SharpVault.Api/Security/ApiAuthenticationExtensions.cs",
        "src/SharpVault.Web/Security/LocalSignInGuard.cs",
        "src/SharpVault.Web/Security/LocalSignInGuardExtensions.cs",
        "src/SharpVault.Web/Security/WebAuthenticationExtensions.cs",
        "src/SharpVault.Web/Account/AccountEndpoints.cs",
        "src/SharpVault.Web/Api/LocalApiAccessTokenProvider.cs",
        "src/SharpVault.Web/Api/SharpVaultApiRegistration.cs",
        "src/SharpVault.AppHost/AppHost.cs",
        "src/SharpVault.AppHost/AppHostResourceNames.cs",
    ];

    public static TheoryData<string> CommentCheckedFiles()
    {
        var data = new TheoryData<string>();
        foreach (var directory in CommentFreeDirectories)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, directory), "*.cs", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path)).Order(StringComparer.Ordinal))
            {
                data.Add(Relative(file));
            }
        }

        foreach (var file in CommentFreeFiles)
        {
            data.Add(file);
        }

        return data;
    }

    public static TheoryData<string> AuthenticationSourcesExceptKeyStore()
    {
        var data = new TheoryData<string>();
        var folder = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Authentication");
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path) && !path.EndsWith(KeyStoreFile, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            data.Add(Relative(file));
        }

        return data;
    }

    [Fact]
    public void Repository_Files_ContainNoUseLocalAccountKey()
    {
        var offenders = new List<string>();
        foreach (var root in new[] { "src", "tests", "docs", ".github/workflows", ".claude/skills" })
        {
            var directory = Path.Combine(RepositoryRoot.Path, root);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var pattern in ScannedExtensions)
            {
                foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
                {
                    if (IsBuildOutput(file) || Path.GetFileName(file) is ThisFile or AppHostGuardFile || file.Contains("node_modules", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (File.ReadAllText(file).Contains(ForbiddenKey, StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add(Relative(file));
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void Appsettings_AnyFile_CommitsNoAzureAdTenantClientIdOrSecret()
    {
        var files = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!document.RootElement.TryGetProperty("AzureAd", out var azureAd))
            {
                continue;
            }

            foreach (var forbidden in new[] { "TenantId", "ClientId", "ClientSecret", "ClientCredentials", "Domain" })
            {
                Assert.False(azureAd.TryGetProperty(forbidden, out _), $"{Relative(file)} commits AzureAd:{forbidden}.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(CommentCheckedFiles))]
    public void AuthenticationSources_Files_ContainNoComments(string relativePath)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, relativePath));

        var comments = lines
            .Select((text, index) => (Text: text, Number: index + 1))
            .Where(line => line.Number > 2 && (InlineComment().IsMatch(line.Text) || line.Text.Contains("/*", StringComparison.Ordinal) || line.Text.Contains("#region", StringComparison.Ordinal)))
            .Select(line => $"{relativePath}:{line.Number}")
            .ToList();

        Assert.NotEmpty(lines);
        Assert.Empty(comments);
        Assert.Equal("// SharpVault - Copyright (c) SwartBergStudio.", lines[0]);
        Assert.Equal("// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.", lines[1]);
    }

    [Theory]
    [MemberData(nameof(AuthenticationSourcesExceptKeyStore))]
    public void AuthenticationProject_Source_UsesNoSystemRandomAndNoFileWritesOutsideTheKeyStore(string relativePath)
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));

        Assert.DoesNotMatch(SystemRandom(), content);
        foreach (var api in FileWriteApis)
        {
            Assert.DoesNotContain(api, content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KeyStore_Source_UsesNoSystemRandom()
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Authentication", "Local", KeyStoreFile));

        Assert.DoesNotMatch(SystemRandom(), content);
        Assert.Contains("RandomNumberGenerator", content, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(^|\s)//")]
    private static partial Regex InlineComment();

    [GeneratedRegex(@"\bnew\s+Random\b|\bRandom\.Shared\b")]
    private static partial Regex SystemRandom();

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot.Path, file).Replace('\\', '/');

    private static bool IsBuildOutput(string path)
    {
        var separator = Path.DirectorySeparatorChar;
        return path.Contains(separator + "obj" + separator, StringComparison.Ordinal)
            || path.Contains(separator + "bin" + separator, StringComparison.Ordinal);
    }
}
