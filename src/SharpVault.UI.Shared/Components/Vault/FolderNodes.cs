// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Components.Vault;

public static class FolderNodes
{
    public const string DamagedName = "Unreadable folder";

    public static IReadOnlyList<FolderNode> Build(IReadOnlyList<FolderDto> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var ids = folders.Select(folder => folder.Id).ToHashSet();
        var byParent = folders.ToLookup(folder => ParentInList(folder, ids));
        var visited = new HashSet<Guid>();
        var roots = BuildLevel(byParent, null, visited);
        foreach (var orphan in folders.Where(folder => !visited.Contains(folder.Id)).ToList())
        {
            if (visited.Add(orphan.Id))
            {
                roots.Add(new FolderNode(orphan, NameOf(orphan), BuildLevel(byParent, orphan.Id, visited)));
            }
        }

        return Sorted(roots);
    }

    public static string NameOf(FolderDto folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        return folder.IsDamaged || string.IsNullOrWhiteSpace(folder.Name) ? DamagedName : folder.Name;
    }

    public static HashSet<Guid> SubtreeIds(IReadOnlyList<FolderDto> folders, Guid rootId)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var subtree = new HashSet<Guid>();
        var pending = new Queue<Guid>([rootId]);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!subtree.Add(current))
            {
                continue;
            }

            foreach (var child in folders.Where(folder => folder.ParentId == current))
            {
                pending.Enqueue(child.Id);
            }
        }

        return subtree;
    }

    public static IReadOnlyList<Guid> AncestorIds(IReadOnlyList<FolderDto> folders, Guid folderId)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var ancestors = new List<Guid>();
        var seen = new HashSet<Guid> { folderId };
        var current = folders.FirstOrDefault(folder => folder.Id == folderId);
        while (current?.ParentId is { } parentId && seen.Add(parentId))
        {
            ancestors.Add(parentId);
            current = folders.FirstOrDefault(folder => folder.Id == parentId);
        }

        return ancestors;
    }

    private static Guid? ParentInList(FolderDto folder, HashSet<Guid> ids) =>
        folder.ParentId is { } parent && parent != folder.Id && ids.Contains(parent) ? parent : null;

    private static List<FolderNode> BuildLevel(ILookup<Guid?, FolderDto> byParent, Guid? parent, HashSet<Guid> visited)
    {
        var nodes = new List<FolderNode>();
        foreach (var folder in byParent[parent])
        {
            if (visited.Add(folder.Id))
            {
                nodes.Add(new FolderNode(folder, NameOf(folder), BuildLevel(byParent, folder.Id, visited)));
            }
        }

        return Sorted(nodes);
    }

    private static List<FolderNode> Sorted(List<FolderNode> nodes) =>
        [.. nodes.OrderBy(node => node.DisplayName, StringComparer.OrdinalIgnoreCase)];
}
