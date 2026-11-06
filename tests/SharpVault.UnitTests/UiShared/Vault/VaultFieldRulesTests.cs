// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class VaultFieldRulesTests
{
    [Theory]
    [InlineData(SecretTypeDto.Login, true)]
    [InlineData(SecretTypeDto.SecureNote, false)]
    [InlineData(SecretTypeDto.ApiKey, true)]
    [InlineData(SecretTypeDto.Card, true)]
    [InlineData(SecretTypeDto.Other, true)]
    public void ShowsCredentialFields_OnlySecureNoteIsFalse(SecretTypeDto type, bool expected)
    {
        Assert.Equal(expected, VaultFieldRules.ShowsCredentialFields(type));
    }

    [Fact]
    public void ShowsCredentialFields_EveryDefinedType_IsCovered()
    {
        var hidden = Enum.GetValues<SecretTypeDto>().Where(type => !VaultFieldRules.ShowsCredentialFields(type)).ToList();

        Assert.Equal([SecretTypeDto.SecureNote], hidden);
    }

    [Fact]
    public void Limits_MatchTheServerVaultLimits()
    {
        Assert.Equal(VaultLimits.NameMaxLength, VaultFieldRules.NameMaxLength);
        Assert.Equal(VaultLimits.UsernameMaxLength, VaultFieldRules.UsernameMaxLength);
        Assert.Equal(VaultLimits.UrlMaxLength, VaultFieldRules.UrlMaxLength);
        Assert.Equal(VaultLimits.ValueMaxLength, VaultFieldRules.ValueMaxLength);
        Assert.Equal(VaultLimits.NotesMaxLength, VaultFieldRules.NotesMaxLength);
        Assert.Equal(VaultLimits.FolderNameMaxLength, VaultFieldRules.FolderNameMaxLength);
    }
}
