// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Components.Vault;

public static class SecretRequestMapper
{
    public const char TagSeparator = ',';

    public static List<string> SplitTags(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split(TagSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    public static string JoinTags(IEnumerable<string> tags) => string.Join(TagSeparator + " ", tags);

    public static UpdateSecretRequestDto ForUpdate(UpdateSecretRequestDto model, string? tagsText)
    {
        ArgumentNullException.ThrowIfNull(model);

        var request = new UpdateSecretRequestDto
        {
            Type = model.Type,
            Name = model.Name.Trim(),
            Username = NullIfBlank(model.Username),
            Url = NullIfBlank(model.Url),
            Tags = SplitTags(tagsText),
            ReplaceNotes = model.ReplaceNotes,
            Notes = model.ReplaceNotes ? NullIfEmpty(model.Notes) : null,
        };

        var replaceValue = model.ReplaceValue && VaultFieldRules.ShowsCredentialFields(model.Type);
        request.ReplaceValue = replaceValue;
        request.Value = replaceValue ? NullIfEmpty(model.Value) : null;
        return request;
    }

    public static CreateSecretRequestDto ForCreate(UpdateSecretRequestDto model, string? tagsText, Guid? folderId)
    {
        ArgumentNullException.ThrowIfNull(model);

        var credentials = VaultFieldRules.ShowsCredentialFields(model.Type);
        return new CreateSecretRequestDto
        {
            Type = model.Type,
            Name = model.Name.Trim(),
            Username = credentials ? NullIfBlank(model.Username) : null,
            Url = credentials ? NullIfBlank(model.Url) : null,
            Value = credentials ? NullIfEmpty(model.Value) : null,
            Notes = NullIfEmpty(model.Notes),
            Tags = SplitTags(tagsText),
            FolderId = folderId,
        };
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}
