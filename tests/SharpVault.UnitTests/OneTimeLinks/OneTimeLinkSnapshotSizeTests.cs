// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkSnapshotSizeTests
{
    private const char EscapedCharacter = '<';
    private const int BytesPerEscapedUnit = 6;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Snapshot_WorstCaseFields_SerializesWithinTheMaximumPayload()
    {
        var snapshot = new OneTimeLinkSnapshotDto
        {
            Type = SecretTypeDto.Login,
            Name = new string(EscapedCharacter, VaultLimits.NameMaxLength),
            Username = new string(EscapedCharacter, VaultLimits.UsernameMaxLength),
            Url = new string(EscapedCharacter, VaultLimits.UrlMaxLength),
            Value = new string(EscapedCharacter, VaultLimits.ValueMaxLength),
            Notes = new string(EscapedCharacter, VaultLimits.NotesMaxLength),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);

        var worstCaseFieldBytes = BytesPerEscapedUnit * (VaultLimits.NameMaxLength + VaultLimits.UsernameMaxLength + VaultLimits.UrlMaxLength + VaultLimits.ValueMaxLength + VaultLimits.NotesMaxLength);
        Assert.True(bytes.Length >= worstCaseFieldBytes);
        Assert.True(bytes.Length <= OneTimeLinkLimits.MaxPayloadBytes);
    }
}
