// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Runtime.ExceptionServices;

namespace SharpVault.Application.Common.Models;

public sealed record VaultWriteResult(StoreWriteResult Result, int CompletedChanges, IReadOnlyList<string?> ETags, ExceptionDispatchInfo? Interruption = null);
