// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.Application.Features.Sharing.Directory;
using SharpVault.Dtos.Directory;
using SharpVault.Dtos.Sharing;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Sharing.Security;

[Trait("Category", "Security")]
public sealed partial class SharingSecurityContractTests
{
    private static readonly string[] KnownSharedOwnerFiles = ["RevealSecretField.cs", "UpdateSecret.cs"];

    private static readonly string[] ForbiddenWords = ["Value", "Notes", "Password", "Key", "Envelope", "Ciphertext", "Nonce", "Secret"];

    private static readonly string[] AllowedProperties = ["HasValue", "HasNotes", "ConcurrencyToken", "ContinuationToken"];

    [GeneratedRegex(@"\bSharedOwnerId\b", RegexOptions.CultureInvariant)]
    private static partial Regex SharedOwnerIdReference();

    [Fact]
    public void SharedOwnerId_IsUsedOnlyByTheKnownHandlers()
    {
        var source = Path.Combine(RepositoryRoot.Path, "src");

        var users = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => SharedOwnerIdReference().IsMatch(File.ReadAllText(path)))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(KnownSharedOwnerFiles, users);
    }

    [Fact]
    public void SharedDtos_NeverExposeValueNotesOrKeyMaterial()
    {
        var types = new[]
        {
            typeof(SharedItemDto),
            typeof(SharedItemPageDto),
            typeof(SharedSecretDto),
            typeof(SharedFolderContentsDto),
            typeof(SharedFolderSummaryDto),
            typeof(ShareGrantDto),
            typeof(ShareGrantListDto),
            typeof(DirectoryUserDto),
            typeof(DirectoryUserListDto),
        };

        var offenders = types
            .SelectMany(type => type.GetProperties().Select(property => (Type: type.Name, Property: property.Name)))
            .Where(pair => !AllowedProperties.Contains(pair.Property, StringComparer.Ordinal))
            .Where(pair => ForbiddenWords.Any(word => pair.Property.Contains(word, StringComparison.Ordinal)))
            .Select(pair => $"{pair.Type}.{pair.Property}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void DirectoryUserDto_Properties_AreOnlyIdNameEmail()
    {
        var names = typeof(DirectoryUserDto).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(["DisplayName", "Email", "Id"], names);
    }

    [Fact]
    public void ShareGrantDto_Properties_ContainNoUserStatus()
    {
        var names = typeof(ShareGrantDto).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain(names, name => name.Contains("Status", StringComparison.Ordinal) || name.Contains("Lock", StringComparison.Ordinal) || name.Contains("Active", StringComparison.Ordinal));
    }

    [Fact]
    public void SearchDirectoryQuery_ToString_ReturnsTypeNameOnly()
    {
        var query = new SearchDirectoryUsersQuery("alice@example.test");

        var text = query.ToString();

        Assert.Equal(nameof(SearchDirectoryUsersQuery), text);
        Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
    }
}
