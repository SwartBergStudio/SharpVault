// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation.Results;
using SharpVault.Application.Features.Vault.History.Commands;
using SharpVault.Application.Features.Vault.History.Queries;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class HistoryValidatorTests
{
    private const string ValidETag = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";

    private readonly RestoreSecretVersionCommandValidator _restore = new();
    private readonly RevealSecretVersionCommandValidator _reveal = new();
    private readonly ListSecretVersionsQueryValidator _list = new();

    private static void AssertFailsOn(ValidationResult result, string property)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == property);
    }

    [Fact]
    public void RestoreSecretVersionCommandValidator_Valid_Passes() =>
        Assert.True(_restore.Validate(new RestoreSecretVersionCommand(Guid.NewGuid(), Guid.NewGuid(), ValidETag)).IsValid);

    [Fact]
    public void RestoreSecretVersionCommandValidator_EmptySecretId_Fails() =>
        AssertFailsOn(_restore.Validate(new RestoreSecretVersionCommand(Guid.Empty, Guid.NewGuid(), ValidETag)), "SecretId");

    [Fact]
    public void RestoreSecretVersionCommandValidator_EmptyVersionId_Fails() =>
        AssertFailsOn(_restore.Validate(new RestoreSecretVersionCommand(Guid.NewGuid(), Guid.Empty, ValidETag)), "VersionId");

    [Fact]
    [Trait("Category", "Security")]
    public void RestoreSecretVersionCommandValidator_WildcardETag_Fails() =>
        AssertFailsOn(_restore.Validate(new RestoreSecretVersionCommand(Guid.NewGuid(), Guid.NewGuid(), "*")), "ETag");

    [Theory]
    [InlineData(RevealIntentDto.View)]
    [InlineData(RevealIntentDto.Copy)]
    public void RevealSecretVersionCommandValidator_DefinedIntent_Passes(RevealIntentDto intent) =>
        Assert.True(_reveal.Validate(new RevealSecretVersionCommand(Guid.NewGuid(), Guid.NewGuid(), intent)).IsValid);

    [Fact]
    public void RevealSecretVersionCommandValidator_EmptySecretId_Fails() =>
        AssertFailsOn(_reveal.Validate(new RevealSecretVersionCommand(Guid.Empty, Guid.NewGuid(), RevealIntentDto.View)), "SecretId");

    [Fact]
    public void RevealSecretVersionCommandValidator_EmptyVersionId_Fails() =>
        AssertFailsOn(_reveal.Validate(new RevealSecretVersionCommand(Guid.NewGuid(), Guid.Empty, RevealIntentDto.View)), "VersionId");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(3)]
    public void RevealSecretVersionCommandValidator_UndefinedIntent_Fails(int intent) =>
        AssertFailsOn(_reveal.Validate(new RevealSecretVersionCommand(Guid.NewGuid(), Guid.NewGuid(), (RevealIntentDto)intent)), "Intent");

    [Fact]
    public void ListSecretVersionsQueryValidator_Valid_Passes() =>
        Assert.True(_list.Validate(new ListSecretVersionsQuery(Guid.NewGuid())).IsValid);

    [Fact]
    public void ListSecretVersionsQueryValidator_EmptySecretId_Fails() =>
        AssertFailsOn(_list.Validate(new ListSecretVersionsQuery(Guid.Empty)), "SecretId");
}
