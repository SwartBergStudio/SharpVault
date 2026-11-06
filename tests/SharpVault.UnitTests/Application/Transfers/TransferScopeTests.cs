// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;

namespace SharpVault.UnitTests.Application.Transfers;

public sealed class TransferScopeTests
{
    private static readonly Guid Root = Guid.Parse("00000000-0000-0000-0000-00000000f0f0");
    private static readonly Guid FolderA = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid FolderB = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid FolderAChild = Guid.Parse("00000000-0000-0000-0000-0000000000ac");
    private static readonly Guid SecretInA = Guid.Parse("00000000-0000-0000-0000-000000000051");
    private static readonly Guid SecretInB = Guid.Parse("00000000-0000-0000-0000-000000000052");
    private static readonly Guid SecretInChild = Guid.Parse("00000000-0000-0000-0000-000000000053");
    private static readonly Guid Unfiled = Guid.Parse("00000000-0000-0000-0000-000000000054");
    private static readonly Guid MissingFolder = Guid.Parse("00000000-0000-0000-0000-0000000000ff");

    [Fact]
    public void AllItems_IncludesEveryRow()
    {
        var scope = TransferScope.Build(Job(TransferScopeKind.AllItems), Rows(), Root);

        Assert.Equal(Rows().Count, scope.InScope.Count);
    }

    [Fact]
    public void Selected_IncludesTheClosureAcrossActiveAndRecycled()
    {
        var scope = TransferScope.Build(Job(TransferScopeKind.SelectedFolders, FolderA), Rows(), Root);

        Assert.Contains(scope.InScope, row => row.ItemId == FolderAChild);
        Assert.Contains(scope.InScope, row => row.ItemId == SecretInChild);
    }

    [Fact]
    public void Selected_ExcludesSiblings()
    {
        var scope = TransferScope.Build(Job(TransferScopeKind.SelectedFolders, FolderA), Rows(), Root);

        Assert.DoesNotContain(scope.InScope, row => row.ItemId == SecretInB || row.ItemId == FolderB || row.ItemId == Unfiled);
    }

    [Fact]
    public void Selected_MissingFolder_AddsNothing()
    {
        var scope = TransferScope.Build(Job(TransferScopeKind.SelectedFolders, MissingFolder), Rows(), Root);

        Assert.Empty(scope.InScope);
    }

    [Fact]
    public void Selected_CycleInStoredData_DoesNotHang()
    {
        var cyclic = new List<VaultInventoryRow>
        {
            Folder(FolderA, parent: FolderAChild),
            Folder(FolderAChild, parent: FolderA),
            Secret(SecretInChild, container: FolderAChild),
        };

        var scope = TransferScope.Build(Job(TransferScopeKind.SelectedFolders, FolderA), cyclic, Root);

        Assert.Contains(scope.InScope, row => row.ItemId == SecretInChild);
    }

    [Fact]
    public void Selected_VersionFollowsItsSecret()
    {
        var version = new VaultInventoryRow(VaultItemKind.Version, Guid.NewGuid(), SecretInA, DateTimeOffset.UnixEpoch, "etag");
        var rows = new List<VaultInventoryRow>(Rows()) { version };

        var scope = TransferScope.Build(Job(TransferScopeKind.SelectedFolders, FolderA), rows, Root);

        Assert.Contains(scope.InScope, row => row.ItemId == version.ItemId);
    }

    [Fact]
    public void MapFolderId_InScope_KeepsTheId()
    {
        var scope = TransferScope.Build(Job(TransferScopeKind.SelectedFolders, FolderA), Rows(), Root);

        Assert.Equal(FolderAChild, scope.MapFolderId(FolderAChild));
    }

    [Fact]
    public void MapFolderId_Null_ReturnsRoot()
    {
        var scope = TransferScope.Build(Job(TransferScopeKind.AllItems), Rows(), Root);

        Assert.Equal(Root, scope.MapFolderId(null));
    }

    [Fact]
    public void MapFolderId_OutOfScopeParent_ReturnsRoot()
    {
        var scope = TransferScope.Build(Job(TransferScopeKind.SelectedFolders, FolderAChild), Rows(), Root);

        Assert.Equal(Root, scope.MapFolderId(FolderA));
    }

    [Fact]
    public void FoldersDeepestFirst_ChildComesBeforeParent()
    {
        var rows = Rows();
        var folders = rows.Where(TransferScope.IsFolder).ToList();

        var ordered = TransferScope.FoldersDeepestFirst(folders, rows).ToList();

        Assert.True(ordered.FindIndex(row => row.ItemId == FolderAChild) < ordered.FindIndex(row => row.ItemId == FolderA));
    }

    private static TransferJob Job(TransferScopeKind kind, params Guid[] folders) => new()
    {
        ScopeKind = kind,
        SelectedFolderIds = folders,
        Category = ReasonCategory.RoleChange,
    };

    private static List<VaultInventoryRow> Rows() =>
    [
        Folder(FolderA, parent: null),
        Folder(FolderB, parent: null),
        Folder(FolderAChild, parent: FolderA),
        Secret(SecretInA, container: FolderA),
        Secret(SecretInB, container: FolderB),
        Secret(SecretInChild, container: FolderAChild),
        Secret(Unfiled, container: null),
    ];

    private static VaultInventoryRow Folder(Guid id, Guid? parent) =>
        new(VaultItemKind.ActiveFolder, id, parent, DateTimeOffset.UnixEpoch, "etag");

    private static VaultInventoryRow Secret(Guid id, Guid? container) =>
        new(VaultItemKind.ActiveSecret, id, container, DateTimeOffset.UnixEpoch, "etag");
}
