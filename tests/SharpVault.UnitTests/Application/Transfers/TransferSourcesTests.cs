// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Transfers;

[Trait("Category", "Security")]
public sealed partial class TransferSourcesTests
{
    private static readonly string[] SourceRoots =
    [
        Path.Combine("src", "SharpVault.Application", "Features", "Transfers"),
        Path.Combine("src", "SharpVault.Infrastructure", "Transfers"),
        Path.Combine("src", "SharpVault.Api", "Endpoints", "Transfers"),
    ];

    [Fact]
    public void TransferSourceRoots_Always_AllExist()
    {
        foreach (var root in SourceRoots)
        {
            Assert.True(Directory.Exists(Path.Combine(RepositoryRoot.Path, root)), $"Missing source root {root}.");
        }
    }

    [Fact]
    public void TransferSources_NeverCallDecrypt()
    {
        var violations = Violations(DecryptCall());

        Assert.Empty(violations);
    }

    [Fact]
    public void TransferSources_NoETagAll()
    {
        var violations = Violations(EtagAll());

        Assert.Empty(violations);
    }

    [Fact]
    public void TransferSources_NoFilterConcatenation()
    {
        var violations = Violations(FilterConcatenation());

        Assert.Empty(violations);
    }

    [Fact]
    public void TransferSources_LogNoIdsAtInformation()
    {
        var violations = Violations(InformationWithId());

        Assert.Empty(violations);
    }

    [Fact]
    public void TransferCipher_NeverCreatesAString()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Application", "Features", "Transfers", "Job", "TransferCipher.cs"));

        Assert.DoesNotMatch(new Regex(@"\bstring\s+\w+\s*=\s*Encoding\.|GetString\(|new string\(", RegexOptions.CultureInvariant), text);
    }

    private static List<string> Violations(Regex pattern)
    {
        var violations = new List<string>();
        foreach (var root in SourceRoots)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, root), "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (pattern.IsMatch(text))
                {
                    violations.Add(Path.GetRelativePath(RepositoryRoot.Path, file));
                }
            }
        }

        return violations;
    }

    [GeneratedRegex(@"\.(Decrypt|TryDecrypt)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex DecryptCall();

    [GeneratedRegex(@"ETag\.All", RegexOptions.CultureInvariant)]
    private static partial Regex EtagAll();

    [GeneratedRegex(@"(Filter|filter)\s*=\s*\$?""[^""]*""\s*\+", RegexOptions.CultureInvariant)]
    private static partial Regex FilterConcatenation();

    [GeneratedRegex(@"LogInformation\([^)]*(Id|id)\b", RegexOptions.CultureInvariant)]
    private static partial Regex InformationWithId();
}
