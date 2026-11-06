// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Users;

namespace SharpVault.UI.Shared.Components.Transfers;

public sealed class TransferFormInput
{
    public const int MaxSelectedFolders = 100;

    public Guid? SourceUserId { get; set; }

    public Guid? TargetUserId { get; set; }

    public ReasonCategoryDto Category { get; set; } = ReasonCategoryDto.Other;

    public TransferScopeKindDto Scope { get; set; } = TransferScopeKindDto.AllItems;

    public HashSet<Guid> FolderIds { get; } = [];

    public string Reason { get; set; } = string.Empty;

    public static bool RequiresLockedSource(ReasonCategoryDto category) =>
        category is ReasonCategoryDto.LeftCompany or ReasonCategoryDto.Dismissed or ReasonCategoryDto.Deceased;

    public static TransferScopeKindDto DefaultScopeFor(ReasonCategoryDto category) =>
        category == ReasonCategoryDto.RoleChange ? TransferScopeKindDto.SelectedFolders : TransferScopeKindDto.AllItems;

    public string? FirstProblem()
    {
        if (SourceUserId is null)
        {
            return "Choose the user whose items are moving.";
        }

        if (TargetUserId is null)
        {
            return "Choose the user who receives the items.";
        }

        if (TargetUserId == SourceUserId)
        {
            return "Choose a different user to receive the items.";
        }

        if (!AdminReasonRules.IsValid(Reason))
        {
            return $"The reason must be between {AdminReasonRules.MinLength} and {AdminReasonRules.MaxLength} characters.";
        }

        if (Scope == TransferScopeKindDto.SelectedFolders && FolderIds.Count == 0)
        {
            return "Select at least one folder.";
        }

        return Scope == TransferScopeKindDto.SelectedFolders && FolderIds.Count > MaxSelectedFolders
            ? $"Select at most {MaxSelectedFolders} folders."
            : null;
    }

    public CreateTransferRequestDto ToRequest() => new()
    {
        SourceUserId = SourceUserId ?? Guid.Empty,
        TargetUserId = TargetUserId ?? Guid.Empty,
        Scope = Scope,
        FolderIds = Scope == TransferScopeKindDto.SelectedFolders ? [.. FolderIds] : null,
        Category = Category,
        Reason = Reason.Trim(),
    };
}
