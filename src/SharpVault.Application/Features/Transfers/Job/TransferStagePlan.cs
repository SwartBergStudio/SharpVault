// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Features.Transfers.Job;

public sealed record TransferStagePlan(
    IReadOnlyList<VaultInventoryRow> Secrets,
    IReadOnlyList<VaultInventoryRow> Folders,
    bool StageRoot,
    IReadOnlyList<StagedItemRef> Removals);
