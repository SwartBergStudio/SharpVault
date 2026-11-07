// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation.Results;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.RecycleBin.Commands;
using SharpVault.Application.Features.Vault.RecycleBin.Queries;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RecycleBinValidatorTests
{
    private readonly ListRecycleBinQueryValidator _list = new();
    private readonly RestoreRecycledSecretCommandValidator _restoreSecret = new();
    private readonly RestoreRecycledFolderCommandValidator _restoreFolder = new();
    private readonly PurgeRecycledSecretCommandValidator _purgeSecret = new();
    private readonly PurgeRecycledFolderCommandValidator _purgeFolder = new();

    private static void AssertFailsOn(ValidationResult result, string property)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == property);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void ListRecycleBinQueryValidator_PageSizeInRange_Passes(int pageSize) =>
        Assert.True(_list.Validate(new ListRecycleBinQuery(pageSize, null)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void ListRecycleBinQueryValidator_PageSizeOutOfRange_Fails(int pageSize) =>
        AssertFailsOn(_list.Validate(new ListRecycleBinQuery(pageSize, null)), "PageSize");

    [Theory]
    [InlineData("f_0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    public void ListRecycleBinQueryValidator_BinKeyToken_Passes(string token) =>
        Assert.True(_list.Validate(new ListRecycleBinQuery(50, token)).IsValid);

    [Fact]
    public void ListRecycleBinQueryValidator_StorageToken_Passes() =>
        Assert.True(_list.Validate(new ListRecycleBinQuery(50, ContinuationTokens.ForStorage("abc"))).IsValid);

    [Fact]
    public void ListRecycleBinQueryValidator_NoToken_Passes() =>
        Assert.True(_list.Validate(new ListRecycleBinQuery(50, null)).IsValid);

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("x_0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789ABCDEF0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcde")]
    [InlineData("s_0123456789abcdef0123456789abcdef\n")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("' or PartitionKey ne '")]
    public void ListRecycleBinQueryValidator_TokenPattern_Fails(string token) =>
        AssertFailsOn(_list.Validate(new ListRecycleBinQuery(50, token)), "ContinuationToken");

    [Fact]
    public void RestoreRecycledSecretCommandValidator_Valid_Passes() =>
        Assert.True(_restoreSecret.Validate(new RestoreRecycledSecretCommand(Guid.NewGuid())).IsValid);

    [Fact]
    public void RestoreRecycledSecretCommandValidator_EmptyId_Fails() =>
        AssertFailsOn(_restoreSecret.Validate(new RestoreRecycledSecretCommand(Guid.Empty)), "SecretId");

    [Fact]
    public void RestoreRecycledFolderCommandValidator_Valid_Passes() =>
        Assert.True(_restoreFolder.Validate(new RestoreRecycledFolderCommand(Guid.NewGuid())).IsValid);

    [Fact]
    public void RestoreRecycledFolderCommandValidator_EmptyId_Fails() =>
        AssertFailsOn(_restoreFolder.Validate(new RestoreRecycledFolderCommand(Guid.Empty)), "FolderId");

    [Fact]
    public void PurgeRecycledSecretCommandValidator_Valid_Passes() =>
        Assert.True(_purgeSecret.Validate(new PurgeRecycledSecretCommand(Guid.NewGuid())).IsValid);

    [Fact]
    public void PurgeRecycledSecretCommandValidator_EmptyId_Fails() =>
        AssertFailsOn(_purgeSecret.Validate(new PurgeRecycledSecretCommand(Guid.Empty)), "SecretId");

    [Fact]
    public void PurgeRecycledFolderCommandValidator_Valid_Passes() =>
        Assert.True(_purgeFolder.Validate(new PurgeRecycledFolderCommand(Guid.NewGuid())).IsValid);

    [Fact]
    public void PurgeRecycledFolderCommandValidator_EmptyId_Fails() =>
        AssertFailsOn(_purgeFolder.Validate(new PurgeRecycledFolderCommand(Guid.Empty)), "FolderId");
}
