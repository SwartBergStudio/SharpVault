// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation.Results;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Secrets.Commands;
using SharpVault.Application.Features.Vault.Secrets.Queries;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class SecretValidatorTests
{
    private const string ValidETag = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";

    private static readonly string ValidToken = ContinuationTokens.ForSecret(Guid.NewGuid());

    private static CreateSecretCommand ValidCreate() => new(
        SecretTypeDto.Login, "GitHub", "octo", "https://github.com/", "p@ss", "notes", ["work", "dev"], Guid.NewGuid());

    private static UpdateSecretCommand ValidUpdate() => new(
        Guid.NewGuid(), ValidETag, SecretTypeDto.Login, "GitHub", "octo", "https://github.com/", ["work"], true, "p@ss", true, "notes");

    private static void AssertFailsOn(ValidationResult result, string property)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == property);
    }

    private readonly CreateSecretCommandValidator _create = new();
    private readonly UpdateSecretCommandValidator _update = new();
    private readonly MoveSecretCommandValidator _move = new();
    private readonly DeleteSecretCommandValidator _delete = new();
    private readonly RevealSecretFieldCommandValidator _reveal = new();
    private readonly ListSecretsQueryValidator _list = new();
    private readonly GetSecretQueryValidator _get = new();

    [Fact]
    public void CreateSecretCommandValidator_FullCommand_Passes() => Assert.True(_create.Validate(ValidCreate()).IsValid);

    [Fact]
    public void CreateSecretCommandValidator_OnlyNameAndType_Passes() =>
        Assert.True(_create.Validate(new CreateSecretCommand(SecretTypeDto.SecureNote, "Note", null, null, null, null, null, null)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void CreateSecretCommandValidator_UndefinedType_Fails(int type) =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Type = (SecretTypeDto)type }), "Type");

    [Fact]
    public void CreateSecretCommandValidator_NullName_FailsWithoutThrowing() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Name = null! }), "Name");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void CreateSecretCommandValidator_BlankName_Fails(string name) =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Name = name }), "Name");

    [Fact]
    public void CreateSecretCommandValidator_NameAtLimit_Passes() =>
        Assert.True(_create.Validate(ValidCreate() with { Name = new string('a', VaultLimits.NameMaxLength) }).IsValid);

    [Fact]
    public void CreateSecretCommandValidator_NameOverLimit_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Name = new string('a', VaultLimits.NameMaxLength + 1) }), "Name");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("a\u0000b")]
    [InlineData("a\nb")]
    [InlineData("a‮b")]
    public void CreateSecretCommandValidator_NameWithControlBidiOrLoneSurrogate_Fails(string name) =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Name = name }), "Name");

    [Fact]
    [Trait("Category", "Security")]
    public void CreateSecretCommandValidator_NameWithLoneSurrogate_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Name = "a" + (char)0xD800 + "b" }), "Name");

    [Fact]
    public void CreateSecretCommandValidator_UsernameAtLimit_Passes() =>
        Assert.True(_create.Validate(ValidCreate() with { Username = new string('u', VaultLimits.UsernameMaxLength) }).IsValid);

    [Fact]
    public void CreateSecretCommandValidator_UsernameOverLimit_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Username = new string('u', VaultLimits.UsernameMaxLength + 1) }), "Username");

    [Fact]
    public void CreateSecretCommandValidator_WhitespaceUsername_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Username = "  " }), "Username");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://host/")]
    [InlineData("/relative")]
    [InlineData("https://exa mple.com/")]
    [InlineData("https://example.com/\n")]
    public void CreateSecretCommandValidator_NonHttpUrl_Fails(string url) =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Url = url }), "Url");

    [Fact]
    public void CreateSecretCommandValidator_UrlOverLimit_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Url = "https://a.test/" + new string('x', VaultLimits.UrlMaxLength) }), "Url");

    [Fact]
    public void CreateSecretCommandValidator_ValueAtLimit_Passes() =>
        Assert.True(_create.Validate(ValidCreate() with { Value = new string('v', VaultLimits.ValueMaxLength) }).IsValid);

    [Fact]
    public void CreateSecretCommandValidator_ValueOverLimit_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Value = new string('v', VaultLimits.ValueMaxLength + 1) }), "Value");

    [Fact]
    public void CreateSecretCommandValidator_EmptyValue_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Value = string.Empty }), "Value");

    [Fact]
    public void CreateSecretCommandValidator_ValueWithNewlinesAndTabs_Passes() =>
        Assert.True(_create.Validate(ValidCreate() with { Value = "line1\r\nline2\tend" }).IsValid);

    [Fact]
    public void CreateSecretCommandValidator_ValueLoneSurrogate_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Value = "a\udc00" }), "Value");

    [Fact]
    public void CreateSecretCommandValidator_NotesOverLimit_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Notes = new string('n', VaultLimits.NotesMaxLength + 1) }), "Notes");

    [Fact]
    public void CreateSecretCommandValidator_TwentyTags_Passes() =>
        Assert.True(_create.Validate(ValidCreate() with { Tags = [.. Enumerable.Range(0, 20).Select(i => "t" + i)] }).IsValid);

    [Fact]
    public void CreateSecretCommandValidator_TwentyOneTags_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Tags = [.. Enumerable.Range(0, 21).Select(i => "t" + i)] }), "Tags");

    [Fact]
    public void CreateSecretCommandValidator_DuplicateTagsIgnoringCase_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Tags = ["Work", "work"] }), "Tags");

    [Fact]
    public void CreateSecretCommandValidator_NullTagElement_FailsWithoutThrowing() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Tags = ["ok", null!] }), "Tags");

    [Fact]
    public void CreateSecretCommandValidator_TagOverFiftyCharacters_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { Tags = [new string('t', VaultLimits.TagMaxLength + 1)] }), "Tags");

    [Fact]
    public void CreateSecretCommandValidator_EmptyFolderId_Fails() =>
        AssertFailsOn(_create.Validate(ValidCreate() with { FolderId = Guid.Empty }), "FolderId");

    [Fact]
    public void CreateSecretCommandValidator_NullFolderId_Passes() =>
        Assert.True(_create.Validate(ValidCreate() with { FolderId = null }).IsValid);

    [Fact]
    public void UpdateSecretCommandValidator_FullCommand_Passes() => Assert.True(_update.Validate(ValidUpdate()).IsValid);

    [Fact]
    public void UpdateSecretCommandValidator_NoReplacements_Passes() =>
        Assert.True(_update.Validate(ValidUpdate() with { ReplaceValue = false, Value = null, ReplaceNotes = false, Notes = null }).IsValid);

    [Fact]
    public void UpdateSecretCommandValidator_ReplaceValueWithNullValue_Passes() =>
        Assert.True(_update.Validate(ValidUpdate() with { ReplaceValue = true, Value = null }).IsValid);

    [Fact]
    public void UpdateSecretCommandValidator_EmptySecretId_Fails() =>
        AssertFailsOn(_update.Validate(ValidUpdate() with { SecretId = Guid.Empty }), "SecretId");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("\"strong\"")]
    [InlineData("W/\"datetime'x'\" or 1=1")]
    public void UpdateSecretCommandValidator_BadETag_Fails(string etag) =>
        AssertFailsOn(_update.Validate(ValidUpdate() with { ETag = etag }), "ETag");

    [Fact]
    public void UpdateSecretCommandValidator_NullETag_FailsWithoutThrowing() =>
        AssertFailsOn(_update.Validate(ValidUpdate() with { ETag = null! }), "ETag");

    [Fact]
    public void UpdateSecretCommandValidator_UndefinedType_Fails() =>
        AssertFailsOn(_update.Validate(ValidUpdate() with { Type = (SecretTypeDto)0 }), "Type");

    [Fact]
    public void UpdateSecretCommandValidator_NullName_FailsWithoutThrowing() =>
        AssertFailsOn(_update.Validate(ValidUpdate() with { Name = null! }), "Name");

    [Fact]
    public void UpdateSecretCommandValidator_NameOverLimit_Fails() =>
        AssertFailsOn(_update.Validate(ValidUpdate() with { Name = new string('a', VaultLimits.NameMaxLength + 1) }), "Name");

    [Fact]
    public void UpdateSecretCommandValidator_ReplaceValueFalseWithValue_Fails()
    {
        var result = _update.Validate(ValidUpdate() with { ReplaceValue = false, Value = "sneaky" });

        AssertFailsOn(result, "Value");
        Assert.Contains(result.Errors, error => error.ErrorMessage == VaultMessages.ValueWithoutReplace);
    }

    [Fact]
    public void UpdateSecretCommandValidator_ReplaceNotesFalseWithNotes_Fails()
    {
        var result = _update.Validate(ValidUpdate() with { ReplaceNotes = false, Notes = "sneaky" });

        AssertFailsOn(result, "Notes");
        Assert.Contains(result.Errors, error => error.ErrorMessage == VaultMessages.NotesWithoutReplace);
    }

    [Fact]
    public void UpdateSecretCommandValidator_ValueOverLimit_Fails() =>
        AssertFailsOn(_update.Validate(ValidUpdate() with { Value = new string('v', VaultLimits.ValueMaxLength + 1) }), "Value");

    [Fact]
    public void MoveSecretCommandValidator_Valid_Passes() =>
        Assert.True(_move.Validate(new MoveSecretCommand(Guid.NewGuid(), ValidETag, Guid.NewGuid())).IsValid);

    [Fact]
    public void MoveSecretCommandValidator_ToRoot_Passes() =>
        Assert.True(_move.Validate(new MoveSecretCommand(Guid.NewGuid(), ValidETag, null)).IsValid);

    [Fact]
    public void MoveSecretCommandValidator_EmptySecretId_Fails() =>
        AssertFailsOn(_move.Validate(new MoveSecretCommand(Guid.Empty, ValidETag, null)), "SecretId");

    [Fact]
    public void MoveSecretCommandValidator_EmptyFolderId_Fails() =>
        AssertFailsOn(_move.Validate(new MoveSecretCommand(Guid.NewGuid(), ValidETag, Guid.Empty)), "FolderId");

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    public void MoveSecretCommandValidator_BadETag_Fails(string etag) =>
        AssertFailsOn(_move.Validate(new MoveSecretCommand(Guid.NewGuid(), etag, null)), "ETag");

    [Fact]
    public void DeleteSecretCommandValidator_Valid_Passes() =>
        Assert.True(_delete.Validate(new DeleteSecretCommand(Guid.NewGuid(), ValidETag)).IsValid);

    [Fact]
    public void DeleteSecretCommandValidator_EmptySecretId_Fails() =>
        AssertFailsOn(_delete.Validate(new DeleteSecretCommand(Guid.Empty, ValidETag)), "SecretId");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("*")]
    [InlineData("")]
    public void DeleteSecretCommandValidator_BadETag_Fails(string etag) =>
        AssertFailsOn(_delete.Validate(new DeleteSecretCommand(Guid.NewGuid(), etag)), "ETag");

    [Theory]
    [InlineData(RevealFieldDto.Value, RevealIntentDto.View)]
    [InlineData(RevealFieldDto.Value, RevealIntentDto.Copy)]
    [InlineData(RevealFieldDto.Notes, RevealIntentDto.View)]
    [InlineData(RevealFieldDto.Notes, RevealIntentDto.Copy)]
    public void RevealSecretFieldCommandValidator_EveryDefinedCombination_Passes(RevealFieldDto field, RevealIntentDto intent) =>
        Assert.True(_reveal.Validate(new RevealSecretFieldCommand(Guid.NewGuid(), field, intent)).IsValid);

    [Fact]
    public void RevealSecretFieldCommandValidator_EmptySecretId_Fails() =>
        AssertFailsOn(_reveal.Validate(new RevealSecretFieldCommand(Guid.Empty, RevealFieldDto.Value, RevealIntentDto.View)), "SecretId");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void RevealSecretFieldCommandValidator_UndefinedField_Fails(int field) =>
        AssertFailsOn(_reveal.Validate(new RevealSecretFieldCommand(Guid.NewGuid(), (RevealFieldDto)field, RevealIntentDto.View)), "Field");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void RevealSecretFieldCommandValidator_UndefinedIntent_Fails(int intent) =>
        AssertFailsOn(_reveal.Validate(new RevealSecretFieldCommand(Guid.NewGuid(), RevealFieldDto.Value, (RevealIntentDto)intent)), "Intent");

    [Fact]
    public void GetSecretQueryValidator_Valid_Passes() => Assert.True(_get.Validate(new GetSecretQuery(Guid.NewGuid())).IsValid);

    [Fact]
    public void GetSecretQueryValidator_EmptyId_Fails() => AssertFailsOn(_get.Validate(new GetSecretQuery(Guid.Empty)), "SecretId");

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(200)]
    public void ListSecretsQueryValidator_PageSizeInRange_Passes(int pageSize) =>
        Assert.True(_list.Validate(new ListSecretsQuery(null, false, null, pageSize, null)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void ListSecretsQueryValidator_PageSizeOutOfRange_Fails(int pageSize) =>
        AssertFailsOn(_list.Validate(new ListSecretsQuery(null, false, null, pageSize, null)), "PageSize");

    [Fact]
    public void ListSecretsQueryValidator_FolderIdAndUnfiled_Fails() =>
        AssertFailsOn(_list.Validate(new ListSecretsQuery(Guid.NewGuid(), true, null, 50, null)), "Unfiled");

    [Fact]
    public void ListSecretsQueryValidator_FolderIdAlone_Passes() =>
        Assert.True(_list.Validate(new ListSecretsQuery(Guid.NewGuid(), false, null, 50, null)).IsValid);

    [Fact]
    public void ListSecretsQueryValidator_UnfiledAlone_Passes() =>
        Assert.True(_list.Validate(new ListSecretsQuery(null, true, null, 50, null)).IsValid);

    [Fact]
    public void ListSecretsQueryValidator_EmptyFolderId_Fails() =>
        AssertFailsOn(_list.Validate(new ListSecretsQuery(Guid.Empty, false, null, 50, null)), "FolderId");

    [Fact]
    public void ListSecretsQueryValidator_SearchAt100_Passes() =>
        Assert.True(_list.Validate(new ListSecretsQuery(null, false, new string('s', 100), 50, null)).IsValid);

    [Fact]
    public void ListSecretsQueryValidator_SearchOver100_Fails() =>
        AssertFailsOn(_list.Validate(new ListSecretsQuery(null, false, new string('s', 101), 50, null)), "Search");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ListSecretsQueryValidator_SearchWhitespace_Fails(string search) =>
        AssertFailsOn(_list.Validate(new ListSecretsQuery(null, false, search, 50, null)), "Search");

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("' or PartitionKey ne '")]
    public void ListSecretsQueryValidator_ODataSyntaxInSearch_Passes(string search) =>
        Assert.True(_list.Validate(new ListSecretsQuery(null, false, search, 50, null)).IsValid);

    [Fact]
    public void ListSecretsQueryValidator_SecretIdToken_Passes() =>
        Assert.True(_list.Validate(new ListSecretsQuery(null, false, null, 50, ValidToken)).IsValid);

    [Fact]
    public void ListSecretsQueryValidator_StorageToken_Passes() =>
        Assert.True(_list.Validate(new ListSecretsQuery(null, false, null, 50, ContinuationTokens.ForStorage("abc"))).IsValid);

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("' or PartitionKey ne '")]
    [InlineData("0123456789ABCDEF0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef\n")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    public void ListSecretsQueryValidator_TokenMalformed_Fails(string token) =>
        AssertFailsOn(_list.Validate(new ListSecretsQuery(null, false, null, 50, token)), "ContinuationToken");
}
