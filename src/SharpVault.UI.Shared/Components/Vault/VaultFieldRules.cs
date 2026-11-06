// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Components.Vault;

public static class VaultFieldRules
{
    public const int NameMaxLength = 200;
    public const int UsernameMaxLength = 256;
    public const int UrlMaxLength = 2048;
    public const int ValueMaxLength = 8192;
    public const int NotesMaxLength = 8192;
    public const int FolderNameMaxLength = 100;

    public static bool ShowsCredentialFields(SecretTypeDto type) => type != SecretTypeDto.SecureNote;
}
