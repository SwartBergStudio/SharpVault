// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Transfers;

internal sealed record ActionGroup(Guid ItemId, IReadOnlyList<TableTransactionAction> Actions, int Bytes);

internal static class ActionGroupPacker
{
    private const string GroupTooLargeMessage = "A staged group does not fit in one transaction.";

    public static bool FitsOneTransaction(int actionCount, int bytes) =>
        actionCount <= VaultStorageLimits.MaxActionsPerTransaction && bytes <= VaultStorageLimits.MaxTransactionPayloadBytes;

    public static List<List<ActionGroup>> Pack(IReadOnlyList<ActionGroup> groups, int reservedActions = 0)
    {
        ArgumentNullException.ThrowIfNull(groups);

        var reservedBytes = reservedActions * VaultStorageLimits.ActionOverheadBytes;
        var actionLimit = VaultStorageLimits.MaxActionsPerTransaction - reservedActions;
        var byteLimit = VaultStorageLimits.MaxTransactionPayloadBytes - reservedBytes;
        var packs = new List<List<ActionGroup>>();
        var current = new List<ActionGroup>();
        var actionCount = 0;
        var bytes = 0;

        foreach (var group in groups)
        {
            if (group.Actions.Count == 0)
            {
                continue;
            }

            if (!FitsOneTransaction(group.Actions.Count + reservedActions, group.Bytes + reservedBytes))
            {
                throw new InvalidOperationException(GroupTooLargeMessage);
            }

            var fits = actionCount + group.Actions.Count <= actionLimit
                && bytes + group.Bytes <= byteLimit;
            if (!fits && current.Count > 0)
            {
                packs.Add(current);
                current = [];
                actionCount = 0;
                bytes = 0;
            }

            current.Add(group);
            actionCount += group.Actions.Count;
            bytes += group.Bytes;
        }

        if (current.Count > 0)
        {
            packs.Add(current);
        }

        return packs;
    }
}
