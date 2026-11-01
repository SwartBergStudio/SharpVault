// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Audit;

namespace SharpVault.Application.Common.Interfaces;

/// <summary>
/// The append-only audit log (AUD-1). Entries can be added but never changed or removed.
/// </summary>
public interface IAuditLog
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken);
}
