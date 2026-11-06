// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;

namespace SharpVault.Infrastructure.Vaults;

internal sealed record PlannedTransaction(
    IReadOnlyList<TableTransactionAction> Actions,
    IReadOnlyList<int> ChangeIndexes,
    IReadOnlyList<int?> PrimaryActionIndexes,
    bool LeadsWithInjectedKeyRingTouch);
