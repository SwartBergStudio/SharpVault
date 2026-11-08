// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class FolderNodesTests
{
    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Child = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Grandchild = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-0000000000d4");

    private static FolderDto Folder(Guid id, Guid? parent, string? name, bool damaged = false) =>
        new() { Id = id, ParentId = parent, Name = name, IsDamaged = damaged };

    [Fact]
    public void Build_NestsChildrenUnderParents()
    {
        var nodes = FolderNodes.Build([Folder(Grandchild, Child, "Gamma"), Folder(Child, Root, "Beta"), Folder(Root, null, "Alpha")]);

        var root = Assert.Single(nodes);
        Assert.Equal(Root, root.Folder.Id);
        var child = Assert.Single(root.Children);
        Assert.Equal(Child, child.Folder.Id);
        var grandchild = Assert.Single(child.Children);
        Assert.Equal(Grandchild, grandchild.Folder.Id);
        Assert.Empty(grandchild.Children);
    }

    [Fact]
    public void Build_MissingParent_IsRootLevel()
    {
        var nodes = FolderNodes.Build([Folder(Child, Guid.NewGuid(), "Orphan"), Folder(Root, null, "Alpha")]);

        Assert.Equal([Root, Child], nodes.Select(node => node.Folder.Id));
    }

    [Fact]
    public void Build_SortsChildrenByNameIgnoringCase()
    {
        var nodes = FolderNodes.Build([Folder(Root, null, "bravo"), Folder(Child, null, "Alpha"), Folder(Grandchild, null, "charlie"), Folder(Other, null, "Delta")]);

        Assert.Equal(["Alpha", "bravo", "charlie", "Delta"], nodes.Select(node => node.DisplayName));
    }

    [Fact]
    public void Build_DamagedFolder_HasUnreadableName()
    {
        var nodes = FolderNodes.Build([Folder(Root, null, "Real name", damaged: true)]);

        Assert.Equal("Unreadable folder", Assert.Single(nodes).DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Build_BlankName_HasUnreadableName(string? name)
    {
        var nodes = FolderNodes.Build([Folder(Root, null, name)]);

        Assert.Equal("Unreadable folder", Assert.Single(nodes).DisplayName);
    }

    [Fact]
    public void Build_FolderIsItsOwnParent_StillAppearsOnce()
    {
        var nodes = FolderNodes.Build([Folder(Root, Root, "Loop")]);

        var node = Assert.Single(nodes);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void Build_ParentCycle_ShowsEveryFolderOnceAndTerminates()
    {
        var nodes = FolderNodes.Build([Folder(Root, Child, "One"), Folder(Child, Root, "Two")]);

        var shown = new List<Guid>();
        void Walk(IReadOnlyList<FolderNode> level)
        {
            foreach (var node in level)
            {
                shown.Add(node.Folder.Id);
                Walk(node.Children);
            }
        }

        Walk(nodes);
        Assert.Equal(2, shown.Count);
        Assert.Equal(2, shown.Distinct().Count());
    }

    [Fact]
    public void AncestorIds_ReturnsParentsNearestFirst()
    {
        var folders = new List<FolderDto> { Folder(Root, null, "A"), Folder(Child, Root, "B"), Folder(Grandchild, Child, "C") };

        Assert.Equal([Child, Root], FolderNodes.AncestorIds(folders, Grandchild));
        Assert.Empty(FolderNodes.AncestorIds(folders, Root));
        Assert.Empty(FolderNodes.AncestorIds(folders, Guid.NewGuid()));
    }

    [Fact]
    public void AncestorIds_ParentCycle_Terminates()
    {
        var folders = new List<FolderDto> { Folder(Root, Child, "A"), Folder(Child, Root, "B") };

        Assert.Equal([Child], FolderNodes.AncestorIds(folders, Root));
    }

    [Fact]
    public void SubtreeIds_IncludesTheRootAndAllDescendantsOnly()
    {
        var folders = new List<FolderDto> { Folder(Root, null, "A"), Folder(Child, Root, "B"), Folder(Grandchild, Child, "C"), Folder(Other, null, "D") };

        var subtree = FolderNodes.SubtreeIds(folders, Child);

        Assert.Equal(new HashSet<Guid> { Child, Grandchild }, subtree);
    }
}
