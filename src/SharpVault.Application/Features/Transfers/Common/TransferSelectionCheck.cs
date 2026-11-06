// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Transfers.Job;

namespace SharpVault.Application.Features.Transfers.Common;

public sealed record TransferSelectionCheck(string? Problem, TransferScope Scope);
