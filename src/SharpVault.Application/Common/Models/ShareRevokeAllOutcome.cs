// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Sharing;

namespace SharpVault.Application.Common.Models;

public sealed record ShareRevokeAllOutcome(IReadOnlyList<ShareGrant> Revoked, bool IsComplete)
{
    public int RevokedCount => Revoked.Count;
}
