// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Vault.Common;

public static class VaultMessages
{
    public const string FolderDepthExceeded = "Folders can be nested at most 10 levels deep.";
    public const string FolderCycle = "A folder cannot be moved into itself or one of its subfolders.";
    public const string FolderLimitReached = "A vault can hold at most 1000 folders.";
    public const string FolderOperationTooLarge = "The operation affects more than 1000 items. Split it into smaller steps.";
    public const string FolderTreeInvalid = "The folder structure is not valid.";
    public const string FolderAndUnfiledConflict = "Choose either a folder or the unfiled view, not both.";
    public const string InvalidContinuationToken = "ContinuationToken is not valid.";
    public const string InvalidETag = "The If-Match header must carry the concurrency token from a previous response.";
    public const string FieldTooLong = "A field is longer than the allowed maximum.";
    public const string FieldRequired = "A required field is missing or empty.";
    public const string InvalidCharacters = "A field contains characters that are not allowed.";
    public const string InvalidUrl = "The URL must be an absolute http or https address.";
    public const string TooManyTags = "A secret can have at most 20 tags.";
    public const string DuplicateTag = "Tags must be unique.";
    public const string ValueWithoutReplace = "Value can only be set when ReplaceValue is true.";
    public const string NotesWithoutReplace = "Notes can only be set when ReplaceNotes is true.";
    public const string DamagedSecretNotEditable = "This item could not be read, so it cannot be edited.";
    public const string DamagedFolderNotRenamable = "This folder could not be read, so it cannot be renamed.";
    public const string NotWellFormedText = "A field is not well-formed text.";
}
