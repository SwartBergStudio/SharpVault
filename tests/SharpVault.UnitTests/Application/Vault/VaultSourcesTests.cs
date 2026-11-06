// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.Dtos.Vault;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Vault;

[Trait("Category", "Security")]
public sealed partial class VaultSourcesTests
{
    private static readonly string[] VaultSourceRoots =
    [
        Path.Combine("src", "SharpVault.Application", "Features", "Vault"),
        Path.Combine("src", "SharpVault.Application", "Features", "Sharing"),
        Path.Combine("src", "SharpVault.Infrastructure", "Sharing"),
        Path.Combine("src", "SharpVault.Infrastructure", "Directory"),
        Path.Combine("src", "SharpVault.Api", "Endpoints", "Sharing"),
        Path.Combine("src", "SharpVault.Infrastructure", "Vaults"),
        Path.Combine("src", "SharpVault.Infrastructure", "Maintenance"),
        Path.Combine("src", "SharpVault.Api", "Endpoints", "Vault"),
        Path.Combine("src", "SharpVault.Api", "RateLimiting"),
    ];

    [GeneratedRegex(@"\bIsAdmin\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsAdminReference();

    private static IEnumerable<string> VaultSourceFiles() =>
        VaultSourceRoots
            .Select(root => Path.Combine(RepositoryRoot.Path, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void VaultSources_DoNotReferenceIsAdmin()
    {
        var files = VaultSourceFiles().ToList();

        var offenders = files
            .Where(path => IsAdminReference().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.True(files.Count > 20, "The vault sources were not found.");
        Assert.Empty(offenders);
    }

    [Fact]
    public void SecretDto_Properties_HaveNoValueOrNotes()
    {
        var names = typeof(SecretDto).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(
            ["ConcurrencyToken", "CreatedAt", "FolderId", "HasNotes", "HasValue", "Id", "IsDamaged", "ModifiedAt", "Name", "Tags", "Type", "Url", "Username"],
            names);
    }

    [Fact]
    public void ListingDtos_NeverExposeValueNotesOrKeyMaterial()
    {
        var listingTypes = new[] { typeof(SecretDto), typeof(SecretPageDto), typeof(FolderDto), typeof(FolderListDto), typeof(RecycleBinItemDto), typeof(RecycleBinPageDto), typeof(SecretVersionDto), typeof(SecretVersionListDto) };
        string[] forbidden = ["Value", "Notes", "Password", "Key", "Envelope", "Ciphertext", "Nonce", "Secret"];

        var offenders = listingTypes
            .SelectMany(type => type.GetProperties().Select(property => (Type: type.Name, Property: property.Name)))
            .Where(pair => pair.Property is not ("HasValue" or "HasNotes" or "Secrets") && forbidden.Any(word => pair.Property.Contains(word, StringComparison.Ordinal)))
            .Select(pair => $"{pair.Type}.{pair.Property}")
            .ToList();

        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"\[LoggerMessage\((?<arguments>[^\]]*)\)\]", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex LoggerMessageAttribute();

    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_@]+)[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex TemplatePlaceholder();

    [Fact]
    public void VaultSourceRoots_Always_AllExist()
    {
        var missing = VaultSourceRoots.Where(root => !Directory.Exists(Path.Combine(RepositoryRoot.Path, root))).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void VaultSources_LogNoIdsAtInformation()
    {
        var files = VaultSourceFiles().ToList();
        var informationMessages = 0;
        var offenders = new List<string>();

        foreach (var path in files)
        {
            var source = File.ReadAllText(path);
            foreach (Match attribute in LoggerMessageAttribute().Matches(source))
            {
                var arguments = attribute.Groups["arguments"].Value;
                if (!arguments.Contains("LogLevel.Information", StringComparison.Ordinal))
                {
                    continue;
                }

                informationMessages++;
                foreach (Match placeholder in TemplatePlaceholder().Matches(arguments))
                {
                    var name = placeholder.Groups["name"].Value;
                    if (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Ids", StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{Path.GetRelativePath(RepositoryRoot.Path, path)}: {{{name}}}");
                    }
                }
            }
        }

        Assert.True(informationMessages >= 2, "The Information log messages were not found.");
        Assert.Empty(offenders);
    }

    [Fact]
    public void VaultSources_DoNotCallTheNonGeneratedInformationLogMethods()
    {
        var offenders = VaultSourceFiles()
            .Where(path => File.ReadAllText(path).Contains(".LogInformation(", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void VaultSources_NeverLogExceptionMessageText()
    {
        var offenders = VaultSourceFiles()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\b(exception|ex)\.Message\b", RegexOptions.CultureInvariant))
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"\baudit\.Write(Many)?Async\(", RegexOptions.CultureInvariant)]
    private static partial Regex RequestBoundAuditWrite();

    [Fact]
    public void MutatingHandlers_WriteTheirAuditThroughTheCommittedPath()
    {
        string[] revealHandlers = ["RevealSecretField.cs", "RevealSecretVersion.cs"];
        var handlers = VaultSourceFiles()
            .Where(path => File.ReadAllText(path).Contains("audit.Write", StringComparison.Ordinal))
            .ToList();

        var committed = handlers.Where(path => !revealHandlers.Contains(Path.GetFileName(path))).ToList();
        var requestBound = committed.Where(path => RequestBoundAuditWrite().IsMatch(File.ReadAllText(path))).Select(path => Path.GetFileName(path)).ToList();
        var revealsOnCommittedPath = handlers
            .Where(path => revealHandlers.Contains(Path.GetFileName(path)))
            .Where(path => File.ReadAllText(path).Contains("WriteCommitted", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .ToList();

        Assert.True(committed.Count >= 12, "The mutating handlers were not found.");
        Assert.Equal(revealHandlers.Length, handlers.Count - committed.Count);
        Assert.Empty(requestBound);
        Assert.Empty(revealsOnCommittedPath);
    }
}
