// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RecycledSubtreeTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _batch = Guid.NewGuid();

    private static Versioned<T> Wrap<T>(T value) => new(value, "etag");

    private static RecycledBatch Batch(IEnumerable<Folder> folders, IEnumerable<Secret> secrets) =>
        new([.. secrets.Select(Wrap)], [.. folders.Select(Wrap)]);

    [Fact]
    public void Of_Root_ReturnsRootFirstThenDescendantsAndTheirSecretsOnly()
    {
        var root = VaultSamples.RecycledFolder(_owner, batch: _batch);
        var child = VaultSamples.RecycledFolder(_owner, parentId: root.Id, batch: _batch);
        var sibling = VaultSamples.RecycledFolder(_owner, batch: _batch);
        var inChild = VaultSamples.Recycled(_owner, batch: _batch);
        inChild.FolderId = child.Id;
        var inSibling = VaultSamples.Recycled(_owner, batch: _batch);
        inSibling.FolderId = sibling.Id;
        var loose = VaultSamples.Recycled(_owner, batch: _batch);

        var subtree = RecycledSubtree.Of(root.Id, Batch([sibling, child, root], [inSibling, loose, inChild]));

        Assert.NotNull(subtree);
        Assert.Equal([root.Id, child.Id], subtree.Folders.Select(folder => folder.Value.Id));
        Assert.Equal([inChild.Id], subtree.Secrets.Select(secret => secret.Value.Id));
    }

    [Fact]
    public void Of_RootNotInBatch_ReturnsNull()
    {
        var other = VaultSamples.RecycledFolder(_owner, batch: _batch);

        Assert.Null(RecycledSubtree.Of(Guid.NewGuid(), Batch([other], [])));
    }

    [Fact]
    public void Of_StoredCycle_ReturnsNull()
    {
        var a = VaultSamples.RecycledFolder(_owner, batch: _batch);
        var b = VaultSamples.RecycledFolder(_owner, parentId: a.Id, batch: _batch);
        a.ParentId = b.Id;

        Assert.Null(RecycledSubtree.Of(a.Id, Batch([a, b], [])));
    }

    [Fact]
    public void Of_NullBatch_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RecycledSubtree.Of(Guid.NewGuid(), null!));
    }

    [Fact]
    public void Of_SecretsAreOrderedById()
    {
        var root = VaultSamples.RecycledFolder(_owner, batch: _batch);
        var secrets = Enumerable.Range(0, 5).Select(_ =>
        {
            var secret = VaultSamples.Recycled(_owner, Guid.CreateVersion7(), _batch);
            secret.FolderId = root.Id;
            return secret;
        }).Reverse().ToList();

        var subtree = RecycledSubtree.Of(root.Id, Batch([root], secrets));

        Assert.NotNull(subtree);
        Assert.Equal(secrets.Select(secret => secret.Id).Order(), subtree.Secrets.Select(secret => secret.Value.Id));
    }
}
