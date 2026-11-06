// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation.Results;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Folders.Commands;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class FolderValidatorTests
{
    private readonly CreateFolderCommandValidator _create = new();
    private readonly RenameFolderCommandValidator _rename = new();
    private readonly MoveFolderCommandValidator _move = new();
    private readonly DeleteFolderCommandValidator _delete = new();

    private static void AssertFailsOn(ValidationResult result, string property)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == property);
    }

    [Fact]
    public void CreateFolderCommandValidator_NameAndParent_Passes() =>
        Assert.True(_create.Validate(new CreateFolderCommand("Finance", Guid.NewGuid())).IsValid);

    [Fact]
    public void CreateFolderCommandValidator_RootLevel_Passes() =>
        Assert.True(_create.Validate(new CreateFolderCommand("Finance", null)).IsValid);

    [Fact]
    public void CreateFolderCommandValidator_NameAtLimit_Passes() =>
        Assert.True(_create.Validate(new CreateFolderCommand(new string('f', VaultLimits.FolderNameMaxLength), null)).IsValid);

    [Fact]
    public void CreateFolderCommandValidator_NameOverLimit_Fails() =>
        AssertFailsOn(_create.Validate(new CreateFolderCommand(new string('f', VaultLimits.FolderNameMaxLength + 1), null)), "Name");

    [Fact]
    public void CreateFolderCommandValidator_NullName_FailsWithoutThrowing() =>
        AssertFailsOn(_create.Validate(new CreateFolderCommand(null!, null)), "Name");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateFolderCommandValidator_BlankName_Fails(string name) =>
        AssertFailsOn(_create.Validate(new CreateFolderCommand(name, null)), "Name");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("a\u0000b")]
    [InlineData("a‮b")]
    [InlineData("a\tb")]
    public void CreateFolderCommandValidator_ControlOrBidiName_Fails(string name) =>
        AssertFailsOn(_create.Validate(new CreateFolderCommand(name, null)), "Name");

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b#c?d")]
    [InlineData("' or PartitionKey ne '")]
    public void CreateFolderCommandValidator_TableKeyAndODataCharactersInName_Pass(string name) =>
        Assert.True(_create.Validate(new CreateFolderCommand(name, null)).IsValid);

    [Fact]
    public void CreateFolderCommandValidator_EmptyParentId_Fails() =>
        AssertFailsOn(_create.Validate(new CreateFolderCommand("Finance", Guid.Empty)), "ParentId");

    [Fact]
    public void RenameFolderCommandValidator_Valid_Passes() =>
        Assert.True(_rename.Validate(new RenameFolderCommand(Guid.NewGuid(), "New")).IsValid);

    [Fact]
    public void RenameFolderCommandValidator_EmptyFolderId_Fails() =>
        AssertFailsOn(_rename.Validate(new RenameFolderCommand(Guid.Empty, "New")), "FolderId");

    [Fact]
    public void RenameFolderCommandValidator_NullName_FailsWithoutThrowing() =>
        AssertFailsOn(_rename.Validate(new RenameFolderCommand(Guid.NewGuid(), null!)), "Name");

    [Fact]
    public void RenameFolderCommandValidator_BlankName_Fails() =>
        AssertFailsOn(_rename.Validate(new RenameFolderCommand(Guid.NewGuid(), "  ")), "Name");

    [Fact]
    public void RenameFolderCommandValidator_NameOverLimit_Fails() =>
        AssertFailsOn(_rename.Validate(new RenameFolderCommand(Guid.NewGuid(), new string('f', VaultLimits.FolderNameMaxLength + 1))), "Name");

    [Fact]
    public void RenameFolderCommandValidator_NameWithBidiOverride_Fails() =>
        AssertFailsOn(_rename.Validate(new RenameFolderCommand(Guid.NewGuid(), "a‮b")), "Name");

    [Fact]
    public void MoveFolderCommandValidator_ToParent_Passes() =>
        Assert.True(_move.Validate(new MoveFolderCommand(Guid.NewGuid(), Guid.NewGuid())).IsValid);

    [Fact]
    public void MoveFolderCommandValidator_ToRoot_Passes() =>
        Assert.True(_move.Validate(new MoveFolderCommand(Guid.NewGuid(), null)).IsValid);

    [Fact]
    public void MoveFolderCommandValidator_EmptyFolderId_Fails() =>
        AssertFailsOn(_move.Validate(new MoveFolderCommand(Guid.Empty, null)), "FolderId");

    [Fact]
    public void MoveFolderCommandValidator_EmptyParentId_Fails() =>
        AssertFailsOn(_move.Validate(new MoveFolderCommand(Guid.NewGuid(), Guid.Empty)), "ParentId");

    [Theory]
    [InlineData(FolderDeleteModeDto.MoveToParent)]
    [InlineData(FolderDeleteModeDto.DeleteContents)]
    public void DeleteFolderCommandValidator_DefinedMode_Passes(FolderDeleteModeDto mode) =>
        Assert.True(_delete.Validate(new DeleteFolderCommand(Guid.NewGuid(), mode)).IsValid);

    [Fact]
    public void DeleteFolderCommandValidator_EmptyFolderId_Fails() =>
        AssertFailsOn(_delete.Validate(new DeleteFolderCommand(Guid.Empty, FolderDeleteModeDto.MoveToParent)), "FolderId");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void DeleteFolderCommandValidator_ModeZero_Fails(int mode) =>
        AssertFailsOn(_delete.Validate(new DeleteFolderCommand(Guid.NewGuid(), (FolderDeleteModeDto)mode)), "Mode");
}
