// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Models;

public sealed class StageFence(Guid targetOwnerId, string eTag)
{
    public Guid TargetOwnerId { get; } = targetOwnerId;

    public string ETag { get; set; } = eTag;
}
