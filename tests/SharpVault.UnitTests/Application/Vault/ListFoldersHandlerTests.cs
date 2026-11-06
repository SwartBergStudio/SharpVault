// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.Folders.Queries;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class ListFoldersHandlerTests : VaultHandlerTestBase
{
    private ListFoldersQueryHandler Handler() => new(Store, KeyRings, Current);

    private Folder Named(string name, Guid? parent = null)
    {
        var folder = NewFolder(parent);
        SecretMetadataCipher.EncryptFolderName(MetadataSession, folder, name);
        return folder;
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsEmptyWithoutFolderQuery()
    {
        GivenNoKeyRing();

        var result = await Handler().Handle(new ListFoldersQuery(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Value!.Items);
        await Store.DidNotReceive().ListFoldersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Folders_AreDecryptedAndSortedByNameIgnoringCase()
    {
        var parent = Named("beta");
        GivenFolders(parent, Named("Alpha"), Named("gamma", parent.Id), Named("Delta"));

        var result = await Handler().Handle(new ListFoldersQuery(), TestContext.Current.CancellationToken);

        Assert.Equal(["Alpha", "beta", "Delta", "gamma"], result.Value!.Items.Select(item => item.Name));
        Assert.Equal(parent.Id, result.Value.Items.Single(item => item.Name == "gamma").ParentId);
    }

    [Fact]
    public async Task Handle_DamagedName_FlaggedNotFailedAndSortedFirst()
    {
        var damaged = Named("hidden");
        damaged.NameEnvelope = Named("other").NameEnvelope;
        GivenFolders(Named("Zeta"), damaged);

        var result = await Handler().Handle(new ListFoldersQuery(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Items[0].IsDamaged);
        Assert.Null(result.Value.Items[0].Name);
        Assert.Equal("Zeta", result.Value.Items[1].Name);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AlwaysListsTheCurrentUsersPartitionOnly()
    {
        GivenFolders(Named("Mine"));

        await Handler().Handle(new ListFoldersQuery(), TestContext.Current.CancellationToken);

        await Store.Received(1).ListFoldersAsync(Owner, Arg.Any<CancellationToken>());
        await Store.DidNotReceive().ListFoldersAsync(Arg.Is<Guid>(id => id != Owner), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnreadableKeyRing_ListsEveryFolderAsDamaged()
    {
        GivenFolders(Named("One"), Named("Two"));
        GivenUnreadableKeyRing();

        var result = await Handler().Handle(new ListFoldersQuery(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, item =>
        {
            Assert.True(item.IsDamaged);
            Assert.Null(item.Name);
        });
    }
}
