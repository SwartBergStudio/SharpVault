// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class RecycledItemEraserTests
{
    private readonly IVaultStore _store = Substitute.For<IVaultStore>();
    private readonly Guid _owner = Guid.NewGuid();

    [Fact]
    public async Task EraseFoldersAsync_NoFolders_ReturnsWrittenWithoutSubmit()
    {
        var result = await EraserFactory.For(_store).EraseFoldersAsync(_owner, [], TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.Written, result.Write.Result);
        Assert.Equal(0, result.Write.CompletedChanges);
        await _store.DidNotReceive().SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EraseFoldersAsync_Folders_SubmitsOnePurgePerFolderWithItsETag()
    {
        var folders = new[]
        {
            new Versioned<SharpVault.Domain.Vaults.Folder>(VaultSamples.RecycledFolder(_owner), "e1"),
            new Versioned<SharpVault.Domain.Vaults.Folder>(VaultSamples.RecycledFolder(_owner), "e2"),
        };
        IReadOnlyList<VaultChange>? sent = null;
        _store.SubmitAsync(_owner, Arg.Do<IReadOnlyList<VaultChange>>(changes => sent = changes), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VaultWriteResult(StoreWriteResult.Conflict, 0, [])));

        var result = await EraserFactory.For(_store).EraseFoldersAsync(_owner, folders, TestContext.Current.CancellationToken);

        Assert.Equal(StoreWriteResult.Conflict, result.Write.Result);
        Assert.NotNull(sent);
        Assert.Equal([VaultChangeKind.PurgeFolder, VaultChangeKind.PurgeFolder], sent.Select(change => change.Kind));
        Assert.Equal(["e1", "e2"], sent.Select(change => change.ETag));
    }

    [Fact]
    public async Task EraseSecretAsync_NullSecret_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => EraserFactory.For(_store).EraseSecretAsync(_owner, null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EraseFoldersAsync_NullFolders_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => EraserFactory.For(_store).EraseFoldersAsync(_owner, null!, TestContext.Current.CancellationToken));
    }
}
