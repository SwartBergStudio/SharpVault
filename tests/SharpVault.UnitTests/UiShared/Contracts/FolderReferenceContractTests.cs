// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Transfers.Common;
using SharpVault.UI.Shared.Components.Transfers;

namespace SharpVault.UnitTests.UiShared.Contracts;

public sealed class FolderReferenceContractTests
{
    private const int ReferenceLength = 8;

    public static TheoryData<Guid> FolderIds() => new()
    {
        Guid.Empty,
        Guid.Parse("00000000-0000-0000-0000-00000000000a"),
        Guid.Parse("9f1c2d3e-4a5b-6c7d-8e9f-0a1b2c3d4e5f"),
        Guid.NewGuid(),
        Guid.NewGuid(),
    };

    [Theory]
    [MemberData(nameof(FolderIds))]
    public void Of_SameFolder_MatchesTheServerReference(Guid folderId) =>
        Assert.Equal(TransferFolderReference.Of(folderId), FolderReference.Of(folderId));

    [Theory]
    [MemberData(nameof(FolderIds))]
    public void Of_AnyFolder_IsEightCharacters(Guid folderId) =>
        Assert.Equal(ReferenceLength, FolderReference.Of(folderId).Length);
}
