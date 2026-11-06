// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Transfers;

public sealed class TransferMarker
{
    public Guid SourceUserId { get; set; }

    public Guid TransferId { get; set; }

    public Guid TargetUserId { get; set; }
}
