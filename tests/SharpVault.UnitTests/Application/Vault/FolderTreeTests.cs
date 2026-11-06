// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class FolderTreeTests
{
    private readonly Guid _owner = Guid.NewGuid();

    private Folder Node(Guid? parent = null, Guid? id = null) => VaultSamples.Folder(_owner, id, parent);

    private List<Folder> Chain(int length)
    {
        var folders = new List<Folder>();
        Guid? parent = null;
        for (var index = 0; index < length; index++)
        {
            var folder = Node(parent);
            folders.Add(folder);
            parent = folder.Id;
        }

        return folders;
    }

    [Fact]
    public void DepthOf_RootLevel_IsOne()
    {
        var root = Node();

        Assert.Equal(1, new FolderTree([root]).DepthOf(root.Id));
    }

    [Fact]
    public void DepthOf_TenLevels_IsTen()
    {
        var chain = Chain(10);

        Assert.Equal(10, new FolderTree(chain).DepthOf(chain[^1].Id));
    }

    [Fact]
    public void HeightOf_Leaf_IsOne()
    {
        var chain = Chain(3);

        Assert.Equal(1, new FolderTree(chain).HeightOf(chain[^1].Id));
    }

    [Fact]
    public void HeightOf_ChainOfThree_IsThree()
    {
        var chain = Chain(3);

        Assert.Equal(3, new FolderTree(chain).HeightOf(chain[0].Id));
    }

    [Fact]
    public void HeightOf_BranchingTree_IsTheLongestBranch()
    {
        var root = Node();
        var shallow = Node(root.Id);
        var middle = Node(root.Id);
        var deep = Node(middle.Id);

        var tree = new FolderTree([root, shallow, middle, deep]);

        Assert.Equal(3, tree.HeightOf(root.Id));
        Assert.Equal(2, tree.HeightOf(middle.Id));
    }

    [Fact]
    public void IsSameOrDescendant_Self_IsTrue()
    {
        var root = Node();

        Assert.True(new FolderTree([root]).IsSameOrDescendant(root.Id, root.Id));
    }

    [Fact]
    public void IsSameOrDescendant_Grandchild_IsTrue()
    {
        var chain = Chain(3);

        Assert.True(new FolderTree(chain).IsSameOrDescendant(chain[2].Id, chain[0].Id));
    }

    [Fact]
    public void IsSameOrDescendant_Ancestor_IsFalse()
    {
        var chain = Chain(3);

        Assert.False(new FolderTree(chain).IsSameOrDescendant(chain[0].Id, chain[2].Id));
    }

    [Fact]
    public void IsSameOrDescendant_Sibling_IsFalse()
    {
        var root = Node();
        var left = Node(root.Id);
        var right = Node(root.Id);

        Assert.False(new FolderTree([root, left, right]).IsSameOrDescendant(left.Id, right.Id));
    }

    [Fact]
    public void IsSameOrDescendant_UnknownCandidate_IsFalse()
    {
        var root = Node();

        Assert.False(new FolderTree([root]).IsSameOrDescendant(Guid.NewGuid(), root.Id));
    }

    [Fact]
    public void SubtreeOf_ReturnsRootFirstThenAllDescendants()
    {
        var root = Node();
        var childA = Node(root.Id);
        var childB = Node(root.Id);
        var grandchild = Node(childA.Id);
        var unrelated = Node();

        var subtree = new FolderTree([unrelated, root, childA, childB, grandchild]).SubtreeOf(root.Id);

        Assert.Equal(root.Id, subtree[0]);
        Assert.Equal(new[] { root.Id, childA.Id, childB.Id, grandchild.Id }.Order(), subtree.Order());
        Assert.DoesNotContain(unrelated.Id, subtree);
    }

    [Fact]
    public void ChildrenOf_Null_ReturnsRootLevelFolders()
    {
        var first = Node();
        var second = Node();
        var child = Node(first.Id);

        var tree = new FolderTree([first, second, child]);

        Assert.Equal(new[] { first.Id, second.Id }.Order(), tree.ChildrenOf(null).Order());
        Assert.Equal([child.Id], tree.ChildrenOf(first.Id));
        Assert.Empty(tree.ChildrenOf(second.Id));
        Assert.Empty(tree.ChildrenOf(Guid.NewGuid()));
    }

    [Fact]
    public void DepthOf_StoredCycle_ThrowsInvalidOperationException()
    {
        var a = Node(id: Guid.NewGuid());
        var b = Node(a.Id);
        a.ParentId = b.Id;

        var tree = new FolderTree([a, b]);

        Assert.Throws<InvalidOperationException>(() => tree.DepthOf(a.Id));
    }

    [Fact]
    public void DepthOf_FolderThatIsItsOwnParent_ThrowsInvalidOperationException()
    {
        var self = Node();
        self.ParentId = self.Id;

        Assert.Throws<InvalidOperationException>(() => new FolderTree([self]).DepthOf(self.Id));
    }

    [Fact]
    public void HeightSubtreeAndAncestry_StoredCycle_ThrowInsteadOfLooping()
    {
        var a = Node();
        var b = Node(a.Id);
        a.ParentId = b.Id;
        var tree = new FolderTree([a, b]);

        Assert.Throws<InvalidOperationException>(() => tree.HeightOf(a.Id));
        Assert.Throws<InvalidOperationException>(() => tree.SubtreeOf(a.Id));
        Assert.Throws<InvalidOperationException>(() => tree.IsSameOrDescendant(a.Id, Guid.NewGuid()));
    }

    [Fact]
    public void FolderWithMissingParent_CountsAsRootLevel()
    {
        var orphan = Node(Guid.NewGuid());

        var tree = new FolderTree([orphan]);

        Assert.Equal(1, tree.DepthOf(orphan.Id));
        Assert.Equal([orphan.Id], tree.ChildrenOf(null));
    }

    [Fact]
    public void EmptyTree_CountIsZero()
    {
        var tree = new FolderTree([]);

        Assert.Equal(0, tree.Count);
        Assert.Empty(tree.ChildrenOf(null));
        Assert.False(tree.Contains(Guid.NewGuid()));
    }

    [Fact]
    public void UnknownFolder_DepthHeightAndSubtree_ThrowArgumentException()
    {
        var tree = new FolderTree([Node()]);
        var unknown = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => tree.DepthOf(unknown));
        Assert.Throws<ArgumentException>(() => tree.HeightOf(unknown));
        Assert.Throws<ArgumentException>(() => tree.SubtreeOf(unknown));
    }

    [Fact]
    public void NullFolders_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FolderTree(null!));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void Constructor_ThousandFolders_BuildsQuickly()
    {
        var folders = Chain(1000);

        var started = System.Diagnostics.Stopwatch.StartNew();
        var tree = new FolderTree(folders);
        var depth = tree.DepthOf(folders[^1].Id);
        started.Stop();

        Assert.Equal(1000, depth);
        Assert.True(started.ElapsedMilliseconds < 2000, $"{started.ElapsedMilliseconds} ms");
    }
}
