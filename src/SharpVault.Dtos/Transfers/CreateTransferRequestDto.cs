// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;
using SharpVault.Dtos.Users;

namespace SharpVault.Dtos.Transfers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTransferRequestDto
{
    public Guid SourceUserId { get; set; }

    public Guid TargetUserId { get; set; }

    public TransferScopeKindDto Scope { get; set; }

    public List<Guid>? FolderIds { get; set; }

    public ReasonCategoryDto Category { get; set; }

    public string Reason { get; set; } = string.Empty;
}
