// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation.Results;
using SharpVault.Application.Features.Transfers.Commands;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;

namespace SharpVault.UnitTests.Application.Transfers;

[Trait("Category", "Security")]
public sealed class TransferCommandValidatorTests
{
    private const string ValidReason = "Role change, the vault moves to the new owner";
    private const string ValidETag = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";
    private const string ControlReason = "Role change, the vault moves\u0000 to the new owner";

    private readonly CreateTransferCommandValidator _create = new();
    private readonly ApproveTransferCommandValidator _approve = new();
    private readonly RejectTransferCommandValidator _reject = new();
    private readonly AbortTransferCommandValidator _abort = new();
    private readonly DismissTransferNoticeCommandValidator _dismiss = new();

    private static void AssertFailsOn(ValidationResult result, string property)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == property);
    }

    private static void AssertFailsWithMessage(ValidationResult result, string message)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == message);
    }

    private static CreateTransferCommand Create(
        Guid? source = null,
        Guid? target = null,
        TransferScopeKind scope = TransferScopeKind.AllItems,
        IReadOnlyList<Guid>? folderIds = null,
        ReasonCategory category = ReasonCategory.RoleChange,
        string reason = ValidReason) =>
        new(source ?? Guid.NewGuid(), target ?? Guid.NewGuid(), scope, folderIds, category, reason);

    private static List<Guid> Folders(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    [Fact]
    public void CreateTransfer_AllItemsWithValidParties_Passes() =>
        Assert.True(_create.Validate(Create()).IsValid);

    [Fact]
    public void CreateTransfer_EmptySource_FailsOnSourceUserId() =>
        AssertFailsOn(_create.Validate(Create(source: Guid.Empty)), nameof(CreateTransferCommand.SourceUserId));

    [Fact]
    public void CreateTransfer_EmptyTarget_FailsOnTargetUserId() =>
        AssertFailsOn(_create.Validate(Create(target: Guid.Empty)), nameof(CreateTransferCommand.TargetUserId));

    [Fact]
    public void CreateTransfer_UndefinedScope_FailsOnScope() =>
        AssertFailsOn(_create.Validate(Create(scope: (TransferScopeKind)99)), nameof(CreateTransferCommand.Scope));

    [Fact]
    public void CreateTransfer_AllItemsWithFolders_FailsFolderSelection() =>
        AssertFailsWithMessage(_create.Validate(Create(folderIds: Folders(1))), TransferMessages.FolderSelectionInvalid);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void CreateTransfer_SelectedFoldersWithoutAnyFolder_FailsFolderSelection(int? count)
    {
        var folders = count is null ? null : Folders(count.Value);

        AssertFailsWithMessage(
            _create.Validate(Create(scope: TransferScopeKind.SelectedFolders, folderIds: folders)),
            TransferMessages.FolderSelectionInvalid);
    }

    [Fact]
    public void CreateTransfer_SelectedFoldersAtTheLimit_Passes() =>
        Assert.True(_create.Validate(Create(scope: TransferScopeKind.SelectedFolders, folderIds: Folders(TransferLimits.MaxSelectedFolders))).IsValid);

    [Fact]
    public void CreateTransfer_SelectedFoldersOverTheLimit_FailsFolderSelection() =>
        AssertFailsWithMessage(
            _create.Validate(Create(scope: TransferScopeKind.SelectedFolders, folderIds: Folders(TransferLimits.MaxSelectedFolders + 1))),
            TransferMessages.FolderSelectionInvalid);

    [Fact]
    public void CreateTransfer_DuplicateFolders_FailsFolderSelection()
    {
        var folder = Guid.NewGuid();

        AssertFailsWithMessage(
            _create.Validate(Create(scope: TransferScopeKind.SelectedFolders, folderIds: [folder, folder])),
            TransferMessages.FolderSelectionInvalid);
    }

    [Fact]
    public void CreateTransfer_EmptyFolderId_FailsFolderSelection() =>
        AssertFailsWithMessage(
            _create.Validate(Create(scope: TransferScopeKind.SelectedFolders, folderIds: [Guid.Empty])),
            TransferMessages.FolderSelectionInvalid);

    [Theory]
    [InlineData(99)]
    public void CreateTransfer_UndefinedCategory_FailsWithCategoryMessage(int category) =>
        AssertFailsWithMessage(_create.Validate(Create(category: (ReasonCategory)category)), UserAdminMessages.CategoryInvalid);

    [Fact]
    public void CreateTransfer_ShortReason_FailsOnReason() =>
        AssertFailsOn(_create.Validate(Create(reason: "too short")), nameof(CreateTransferCommand.Reason));

    [Fact]
    public void CreateTransfer_ReasonWithControlCharacter_FailsNotAllowed() =>
        AssertFailsWithMessage(_create.Validate(Create(reason: ControlReason)), UserAdminMessages.ReasonNotAllowed);

    [Fact]
    public void ApproveTransfer_ValidIdAndETag_Passes() =>
        Assert.True(_approve.Validate(new ApproveTransferCommand(Guid.NewGuid(), ValidETag)).IsValid);

    [Fact]
    public void ApproveTransfer_EmptyTransferId_FailsOnTransferId() =>
        AssertFailsOn(_approve.Validate(new ApproveTransferCommand(Guid.Empty, ValidETag)), nameof(ApproveTransferCommand.TransferId));

    [Theory]
    [InlineData(null)]
    [InlineData("*")]
    [InlineData("\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"")]
    [InlineData("abc")]
    [InlineData("W/\"datetime'x' or PartitionKey ne 'y'\"")]
    public void ApproveTransfer_InvalidETag_FailsOnETag(string? etag) =>
        AssertFailsOn(_approve.Validate(new ApproveTransferCommand(Guid.NewGuid(), etag!)), nameof(ApproveTransferCommand.ETag));

    [Fact]
    public void RejectTransfer_ValidCommand_Passes() =>
        Assert.True(_reject.Validate(new RejectTransferCommand(Guid.NewGuid(), ValidETag, ValidReason)).IsValid);

    [Fact]
    public void RejectTransfer_EmptyTransferId_FailsOnTransferId() =>
        AssertFailsOn(_reject.Validate(new RejectTransferCommand(Guid.Empty, ValidETag, ValidReason)), nameof(RejectTransferCommand.TransferId));

    [Fact]
    public void RejectTransfer_WildcardETag_FailsOnETag() =>
        AssertFailsOn(_reject.Validate(new RejectTransferCommand(Guid.NewGuid(), "*", ValidReason)), nameof(RejectTransferCommand.ETag));

    [Fact]
    public void RejectTransfer_ShortReason_FailsOnReason() =>
        AssertFailsOn(_reject.Validate(new RejectTransferCommand(Guid.NewGuid(), ValidETag, "no")), nameof(RejectTransferCommand.Reason));

    [Fact]
    public void RejectTransfer_ReasonWithControlCharacter_FailsNotAllowed() =>
        AssertFailsWithMessage(
            _reject.Validate(new RejectTransferCommand(Guid.NewGuid(), ValidETag, ControlReason)),
            UserAdminMessages.ReasonNotAllowed);

    [Fact]
    public void AbortTransfer_ValidCommand_Passes() =>
        Assert.True(_abort.Validate(new AbortTransferCommand(Guid.NewGuid(), ValidETag, ValidReason)).IsValid);

    [Fact]
    public void AbortTransfer_EmptyTransferId_FailsOnTransferId() =>
        AssertFailsOn(_abort.Validate(new AbortTransferCommand(Guid.Empty, ValidETag, ValidReason)), nameof(AbortTransferCommand.TransferId));

    [Fact]
    public void AbortTransfer_MissingETag_FailsOnETag() =>
        AssertFailsOn(_abort.Validate(new AbortTransferCommand(Guid.NewGuid(), null!, ValidReason)), nameof(AbortTransferCommand.ETag));

    [Fact]
    public void DismissTransferNotice_ValidId_Passes() =>
        Assert.True(_dismiss.Validate(new DismissTransferNoticeCommand(Guid.NewGuid())).IsValid);

    [Fact]
    public void DismissTransferNotice_EmptyId_FailsOnTransferId() =>
        AssertFailsOn(_dismiss.Validate(new DismissTransferNoticeCommand(Guid.Empty)), nameof(DismissTransferNoticeCommand.TransferId));
}
