// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Encryption;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Application.Models;

public sealed class VaultChangeTests
{
    private const string ETag = "W/\"datetime'2026-01-01T00%3A00%3A00.0000000Z'\"";

    private static readonly DateTimeOffset Deleted = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecycleSecret_WithoutDeletionFields_Throws()
    {
        Assert.Throws<ArgumentException>(() => VaultChange.RecycleSecret(NewSecret(), ETag));
    }

    [Fact]
    public void RecycleSecret_WithOnlyDeletedAt_Throws()
    {
        var secret = NewSecret();
        secret.DeletedAt = Deleted;

        Assert.Throws<ArgumentException>(() => VaultChange.RecycleSecret(secret, ETag));
    }

    [Fact]
    public void RecycleSecret_WithBothDeletionFields_Succeeds()
    {
        var secret = NewSecret();
        secret.DeletedAt = Deleted;
        secret.DeletionBatchId = Guid.NewGuid();

        var change = VaultChange.RecycleSecret(secret, ETag);

        Assert.Equal(VaultChangeKind.RecycleSecret, change.Kind);
    }

    [Fact]
    public void RestoreSecret_WithDeletedAt_Throws()
    {
        var secret = NewSecret();
        secret.DeletedAt = Deleted;

        Assert.Throws<ArgumentException>(() => VaultChange.RestoreSecret(secret, ETag));
    }

    [Fact]
    public void RestoreSecret_WithBatchId_Throws()
    {
        var secret = NewSecret();
        secret.DeletionBatchId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => VaultChange.RestoreSecret(secret, ETag));
    }

    [Fact]
    public void RestoreSecret_WithClearedFields_Succeeds()
    {
        var change = VaultChange.RestoreSecret(NewSecret(), ETag);

        Assert.Equal(VaultChangeKind.RestoreSecret, change.Kind);
    }

    [Fact]
    public void RecycleFolder_WithoutDeletionFields_Throws()
    {
        Assert.Throws<ArgumentException>(() => VaultChange.RecycleFolder(NewFolder(), ETag));
    }

    [Fact]
    public void RecycleFolder_WithOnlyBatchId_Throws()
    {
        var folder = NewFolder();
        folder.DeletionBatchId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => VaultChange.RecycleFolder(folder, ETag));
    }

    [Fact]
    public void RecycleFolder_WithBothDeletionFields_Succeeds()
    {
        var folder = NewFolder();
        folder.DeletedAt = Deleted;
        folder.DeletionBatchId = Guid.NewGuid();

        Assert.Equal(VaultChangeKind.RecycleFolder, VaultChange.RecycleFolder(folder, ETag).Kind);
    }

    [Fact]
    public void RestoreFolder_WithDeletionFields_Throws()
    {
        var folder = NewFolder();
        folder.DeletedAt = Deleted;
        folder.DeletionBatchId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => VaultChange.RestoreFolder(folder, ETag));
    }

    [Fact]
    public void RestoreFolder_WithClearedFields_Succeeds()
    {
        Assert.Equal(VaultChangeKind.RestoreFolder, VaultChange.RestoreFolder(NewFolder(), ETag).Kind);
    }

    private static Secret NewSecret() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        DataKey = new WrappedDataKey("id", "alg", new byte[4]),
    };

    private static Folder NewFolder() => new() { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid() };
}
