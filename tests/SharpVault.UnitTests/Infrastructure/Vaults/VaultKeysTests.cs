// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Time.Testing;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

public sealed class VaultKeysTests
{
    [Fact]
    public void PartitionKey_Owner_IsLowercaseHyphenatedGuid()
    {
        var owner = Guid.NewGuid();

        Assert.Equal(owner.ToString("D"), VaultKeys.PartitionKey(owner));
    }

    [Fact]
    public void SecretRowKey_ActiveAndRecycled_HaveExpectedPrefixes()
    {
        var id = Guid.NewGuid();

        Assert.Equal("s_" + id.ToString("N"), VaultKeys.SecretRowKey(id, recycled: false));
        Assert.Equal("bs_" + id.ToString("N"), VaultKeys.SecretRowKey(id, recycled: true));
    }

    [Fact]
    public void FolderRowKey_ActiveAndRecycled_HaveExpectedPrefixes()
    {
        var id = Guid.NewGuid();

        Assert.Equal("f_" + id.ToString("N"), VaultKeys.FolderRowKey(id, recycled: false));
        Assert.Equal("bf_" + id.ToString("N"), VaultKeys.FolderRowKey(id, recycled: true));
    }

    [Fact]
    public void VersionRowKey_Format_IsHSecretUnderscoreVersion()
    {
        var secret = Guid.NewGuid();
        var version = Guid.NewGuid();

        Assert.Equal($"h_{secret:N}_{version:N}", VaultKeys.VersionRowKey(secret, version));
        Assert.Equal($"h_{secret:N}_", VaultKeys.VersionRangeStart(secret));
    }

    [Fact]
    public void VersionRowKey_BelongsToItsSecretRangeOnly()
    {
        var secret = Guid.NewGuid();
        var other = Guid.NewGuid();
        var start = VaultKeys.VersionRangeStart(secret);
        var end = VaultKeys.UpperBound(start);

        var own = VaultKeys.VersionRowKey(secret, Guid.NewGuid());
        var foreign = VaultKeys.VersionRowKey(other, Guid.NewGuid());

        Assert.True(string.CompareOrdinal(own, start) >= 0 && string.CompareOrdinal(own, end) < 0);
        Assert.False(string.CompareOrdinal(foreign, start) >= 0 && string.CompareOrdinal(foreign, end) < 0);
    }

    [Theory]
    [InlineData("s_", "s`")]
    [InlineData("f_", "f`")]
    [InlineData("a", "b")]
    public void UpperBound_Prefix_IncrementsLastCharacter(string prefix, string expected)
    {
        Assert.Equal(expected, VaultKeys.UpperBound(prefix));
    }

    [Fact]
    public void UpperBound_VersionRangeStart_IncrementsTrailingUnderscore()
    {
        var secret = Guid.NewGuid();

        Assert.Equal($"h_{secret:N}`", VaultKeys.UpperBound(VaultKeys.VersionRangeStart(secret)));
    }

    [Theory]
    [InlineData("")]
    public void UpperBound_Empty_Throws(string prefix)
    {
        Assert.Throws<ArgumentException>(() => VaultKeys.UpperBound(prefix));
    }

    [Fact]
    public void UpperBound_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VaultKeys.UpperBound(null!));
    }

    [Fact]
    public void ActiveSecretRange_DoesNotIncludeRecycledOrOtherRows()
    {
        var upper = VaultKeys.UpperBound(VaultKeys.SecretPrefix);
        var id = Guid.NewGuid();

        string[] outside = [VaultKeys.SecretRowKey(id, recycled: true), VaultKeys.FolderRowKey(id, recycled: false), VaultKeys.KeyRingRowKey, VaultKeys.VersionRowKey(id, id)];

        Assert.True(string.CompareOrdinal(VaultKeys.SecretRowKey(id, recycled: false), VaultKeys.SecretPrefix) >= 0);
        Assert.True(string.CompareOrdinal(VaultKeys.SecretRowKey(id, recycled: false), upper) < 0);
        Assert.All(outside, key => Assert.False(string.CompareOrdinal(key, VaultKeys.SecretPrefix) >= 0 && string.CompareOrdinal(key, upper) < 0, key));
    }

    [Fact]
    public void RecycleBinRange_IncludesRecycledSecretsAndFoldersOnly()
    {
        var id = Guid.NewGuid();

        string[] inside = [VaultKeys.SecretRowKey(id, recycled: true), VaultKeys.FolderRowKey(id, recycled: true)];
        string[] outside = [VaultKeys.SecretRowKey(id, recycled: false), VaultKeys.FolderRowKey(id, recycled: false), VaultKeys.KeyRingRowKey, VaultKeys.VersionRowKey(id, id)];

        Assert.All(inside, key => Assert.True(InBin(key), key));
        Assert.All(outside, key => Assert.False(InBin(key), key));
    }

    [Fact]
    public void FolderKey_Null_IsRoot()
    {
        Assert.Equal("root", VaultKeys.FolderKey(null));
    }

    [Fact]
    public void FolderKey_Id_IsNFormat()
    {
        var id = Guid.NewGuid();

        Assert.Equal(id.ToString("N"), VaultKeys.FolderKey(id));
        Assert.Equal(id, VaultKeys.ParseFolderKey(VaultKeys.FolderKey(id)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("root")]
    [InlineData("not-a-guid")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef")]
    [InlineData("{0123456789abcdef0123456789abcdef}")]
    public void ParseFolderKey_InvalidOrRoot_ReturnsNull(string? key)
    {
        Assert.Null(VaultKeys.ParseFolderKey(key));
    }

    [Fact]
    public void Version7Ids_NFormat_SortByCreationTime()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
        var ids = new List<string>();
        for (var index = 0; index < 50; index++)
        {
            ids.Add(Guid.CreateVersion7(time.GetUtcNow()).ToString("N"));
            time.Advance(TimeSpan.FromMilliseconds(2));
        }

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    private static bool InBin(string rowKey) =>
        string.CompareOrdinal(rowKey, VaultKeys.RecycleBinLowerBound) >= 0 && string.CompareOrdinal(rowKey, VaultKeys.RecycleBinUpperBound) < 0;
}
