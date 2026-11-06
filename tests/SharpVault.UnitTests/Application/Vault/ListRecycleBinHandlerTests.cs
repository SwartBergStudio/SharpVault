// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.RecycleBin.Queries;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class ListRecycleBinHandlerTests : VaultHandlerTestBase
{
    private ListRecycleBinQueryHandler Handler() => new(Store, KeyRings, Current);

    private void GivenBin(string? token, params RecycleBinEntry[] entries) =>
        Store.ListRecycleBinAsync(Owner, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RecycleBinPage(entries, token)));

    private static RecycleBinEntry Entry(Secret secret, string etag = "W/\"datetime'x'\"") => new(new Versioned<Secret>(secret, etag), null);

    private static RecycleBinEntry Entry(Folder folder, string etag = "W/\"datetime'x'\"") => new(null, new Versioned<Folder>(folder, etag));

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsEmptyWithoutBinQuery()
    {
        GivenNoKeyRing();

        var result = await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        await Store.DidNotReceive().ListRecycleBinAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SecretAndFolder_AreMappedWithKindNameAndPurgeDate()
    {
        var batch = Guid.NewGuid();
        var secret = RecycledSecretIn(batch, null);
        secret.Type = SecretType.ApiKey;
        SecretMetadataCipher.Encrypt(MetadataSession, secret, new SecretMetadata("Deleted key", null, null, []));
        var folder = RecycledFolderIn(batch);
        SecretMetadataCipher.EncryptFolderName(MetadataSession, folder, "Deleted folder");
        GivenBin(null, Entry(secret), Entry(folder));

        var result = await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        var items = result.Value!.Items;
        Assert.Equal(2, items.Count);
        var secretItem = items[0];
        Assert.Equal(RecycleBinItemKindDto.Secret, secretItem.Kind);
        Assert.Equal(secret.Id, secretItem.Id);
        Assert.Equal("Deleted key", secretItem.Name);
        Assert.Equal(SecretTypeDto.ApiKey, secretItem.Type);
        Assert.Equal(batch, secretItem.DeletionBatchId);
        Assert.Equal(secret.DeletedAt, secretItem.DeletedAt);
        Assert.Equal(secret.DeletedAt!.Value.AddDays(30), secretItem.PurgeAfter);
        Assert.False(secretItem.IsDamaged);
        var folderItem = items[1];
        Assert.Equal(RecycleBinItemKindDto.Folder, folderItem.Kind);
        Assert.Equal(folder.Id, folderItem.Id);
        Assert.Equal("Deleted folder", folderItem.Name);
        Assert.Null(folderItem.Type);
        Assert.Equal(batch, folderItem.DeletionBatchId);
        Assert.Equal(folder.DeletedAt!.Value.AddDays(30), folderItem.PurgeAfter);
    }

    [Fact]
    public async Task Handle_PageToken_PassesThroughBothWays()
    {
        GivenBin("s_0123456789abcdef0123456789abcdef", Entry(RecycledSecretIn(Guid.NewGuid(), null)));
        var incoming = "f_fedcba9876543210fedcba9876543210";

        var result = await Handler().Handle(new ListRecycleBinQuery(25, incoming), TestContext.Current.CancellationToken);

        Assert.Equal("s_0123456789abcdef0123456789abcdef", result.Value!.ContinuationToken);
        await Store.Received(1).ListRecycleBinAsync(Owner, 25, incoming, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LastPage_ReturnsNullToken()
    {
        GivenBin(null, Entry(RecycledSecretIn(Guid.NewGuid(), null)));

        var result = await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        Assert.Null(result.Value!.ContinuationToken);
    }

    [Fact]
    public async Task Handle_EmptyPageWithMoreData_ReturnsEmptyItemsAndTheToken()
    {
        GivenBin(ContinuationTokens.ForStorage("next"));

        var result = await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        Assert.Empty(result.Value!.Items);
        Assert.Equal(ContinuationTokens.ForStorage("next"), result.Value.ContinuationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DamagedEntries_AreFlaggedWithoutNames()
    {
        var secret = RecycledSecretIn(Guid.NewGuid(), null);
        secret.NameEnvelope = RecycledSecretIn(Guid.NewGuid(), null).NameEnvelope;
        var folder = RecycledFolderIn(Guid.NewGuid());
        folder.NameEnvelope = RecycledFolderIn(Guid.NewGuid()).NameEnvelope;
        GivenBin(null, Entry(secret), Entry(folder));

        var result = await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        Assert.All(result.Value!.Items, item =>
        {
            Assert.True(item.IsDamaged);
            Assert.Null(item.Name);
        });
    }

    [Fact]
    public async Task Handle_EntryWithNeitherSecretNorFolder_Throws()
    {
        GivenBin(null, new RecycleBinEntry(null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AlwaysListsTheCurrentUsersBinOnly()
    {
        GivenBin(null);

        await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        await Store.Received(1).ListRecycleBinAsync(Owner, 50, null, Arg.Any<CancellationToken>());
        await Store.DidNotReceive().ListRecycleBinAsync(Arg.Is<Guid>(id => id != Owner), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Items_NeverCarryValueOrNotes()
    {
        var secret = RecycledSecretIn(Guid.NewGuid(), null);
        var value = DecryptCurrentValue(secret);
        GivenBin(null, Entry(secret));

        var result = await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(value, JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_OpensMetadataSessionOncePerRequest()
    {
        var batch = Guid.NewGuid();
        GivenBin(null, [.. Enumerable.Range(0, 20).Select(_ => Entry(RecycledSecretIn(batch, null)))]);
        var baseline = Harness.Provider.UnwrapCount;

        await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        Assert.Equal(baseline + 1, Harness.Provider.UnwrapCount);
    }

    [Fact]
    public async Task Handle_UnreadableKeyRing_ListsEveryEntryAsDamaged()
    {
        var batch = Guid.NewGuid();
        GivenBin(null, Entry(RecycledSecretIn(batch, null)), Entry(RecycledFolderIn(batch)));
        GivenUnreadableKeyRing();

        var result = await Handler().Handle(new ListRecycleBinQuery(50, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, item =>
        {
            Assert.True(item.IsDamaged);
            Assert.Null(item.Name);
        });
    }

    [Fact]
    public async Task Handle_StoreRejectsTheContinuationToken_ReturnsRejectedInvalidToken()
    {
        Store.ListRecycleBinAsync(Owner, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<RecycleBinPage>(_ => throw new InvalidContinuationTokenException());

        var result = await Handler().Handle(new ListRecycleBinQuery(50, ContinuationTokens.ForStorage("forged")), TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(VaultMessages.InvalidContinuationToken, result.Error);
    }
}
