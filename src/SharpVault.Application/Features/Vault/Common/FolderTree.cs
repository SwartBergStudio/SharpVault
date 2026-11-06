// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Vault.Common;

public sealed class FolderTree
{
    private const string UnknownFolderMessage = "The folder is not in the tree.";
    private const string CycleMessage = "The stored folder structure contains a cycle.";

    private readonly Dictionary<Guid, Guid?> _parents;
    private readonly Dictionary<Guid, List<Guid>> _children = [];
    private readonly List<Guid> _roots = [];

    public FolderTree(IEnumerable<Folder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var list = folders.ToList();
        var ids = list.Select(folder => folder.Id).ToHashSet();
        _parents = list.ToDictionary(
            folder => folder.Id,
            folder => folder.ParentId is { } parent && ids.Contains(parent) ? parent : (Guid?)null);

        foreach (var folder in list)
        {
            var parent = _parents[folder.Id];
            if (parent is null)
            {
                _roots.Add(folder.Id);
            }
            else
            {
                AddChild(parent.Value, folder.Id);
            }
        }
    }

    public int Count => _parents.Count;

    public bool Contains(Guid id) => _parents.ContainsKey(id);

    public IReadOnlyList<Guid> ChildrenOf(Guid? parentId)
    {
        if (parentId is null)
        {
            return _roots;
        }

        return _children.TryGetValue(parentId.Value, out var children) ? children : [];
    }

    public int DepthOf(Guid id)
    {
        RequireKnown(id);

        var depth = 1;
        var current = _parents[id];
        while (current is { } parent)
        {
            depth++;
            if (depth > Count)
            {
                throw new InvalidOperationException(CycleMessage);
            }

            current = _parents[parent];
        }

        return depth;
    }

    public int HeightOf(Guid id)
    {
        RequireKnown(id);

        var height = 0;
        var level = new List<Guid> { id };
        var visited = 0;
        while (level.Count > 0)
        {
            height++;
            visited += level.Count;
            if (visited > Count)
            {
                throw new InvalidOperationException(CycleMessage);
            }

            level = [.. level.SelectMany(node => ChildrenOf(node))];
        }

        return height;
    }

    public bool IsSameOrDescendant(Guid candidateId, Guid ancestorId)
    {
        if (!Contains(candidateId))
        {
            return false;
        }

        var current = (Guid?)candidateId;
        var steps = 0;
        while (current is { } node)
        {
            if (node == ancestorId)
            {
                return true;
            }

            steps++;
            if (steps > Count)
            {
                throw new InvalidOperationException(CycleMessage);
            }

            current = _parents[node];
        }

        return false;
    }

    public IReadOnlyList<Guid> SubtreeOf(Guid id)
    {
        RequireKnown(id);

        var result = new List<Guid> { id };
        for (var index = 0; index < result.Count; index++)
        {
            result.AddRange(ChildrenOf(result[index]));
            if (result.Count > Count)
            {
                throw new InvalidOperationException(CycleMessage);
            }
        }

        return result;
    }

    private void AddChild(Guid parent, Guid child)
    {
        if (!_children.TryGetValue(parent, out var children))
        {
            children = [];
            _children[parent] = children;
        }

        children.Add(child);
    }

    private void RequireKnown(Guid id)
    {
        if (!Contains(id))
        {
            throw new ArgumentException(UnknownFolderMessage, nameof(id));
        }
    }
}
