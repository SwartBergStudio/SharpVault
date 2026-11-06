// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Transfers.Common;

namespace SharpVault.UnitTests.Application.Transfers;

public sealed class TransferFolderNameTests
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 5, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Build_SourceNameAndDate_HasTheDocumentedShape()
    {
        var name = TransferFolderName.Build("Ada Lovelace", DecidedAt);

        Assert.Equal("Transferred from Ada Lovelace (2026-11-05)", name);
    }

    [Fact]
    public void Build_LongDisplayName_FitsTheLimit()
    {
        var name = TransferFolderName.Build(new string('n', 2000), DecidedAt);

        Assert.True(name.Length <= VaultLimits.FolderNameMaxLength);
        Assert.EndsWith(" (2026-11-05)", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SurrogatePairAtTheCut_IsNotSplit()
    {
        var prefixLength = "Transferred from ".Length + " (2026-11-05)".Length;
        var room = VaultLimits.FolderNameMaxLength - prefixLength;
        var name = new string('a', room - 1) + "😀" + "tail";

        var built = TransferFolderName.Build(name, DecidedAt);

        Assert.DoesNotContain(built, character => char.IsSurrogate(character));
        Assert.True(built.Length <= VaultLimits.FolderNameMaxLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_BlankName_UsesFallback(string? displayName)
    {
        var name = TransferFolderName.Build(displayName, DecidedAt);

        Assert.Equal("Transferred from a former user (2026-11-05)", name);
    }
}
