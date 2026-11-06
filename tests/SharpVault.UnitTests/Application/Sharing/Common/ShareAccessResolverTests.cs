// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.Common;

[Trait("Category", "Security")]
public sealed class ShareAccessResolverTests
{
    private readonly StubCurrentUser _caller = new();
    private readonly Guid _secretId = Guid.NewGuid();
    private readonly ShareChainWorld _world;

    public ShareAccessResolverTests()
    {
        _world = new ShareChainWorld { CallerId = _caller.UserId };
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ForSecretAsync_NoGrants_ReturnsNoneWithTwoReads()
    {
        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.Equal(SecretPermission.None, result);
        Assert.Equal(2, _world.TotalReads);
    }

    [Theory]
    [InlineData(SharePermission.View, SecretPermission.View)]
    [InlineData(SharePermission.Edit, SecretPermission.Edit)]
    [InlineData(SharePermission.Manage, SecretPermission.Manage)]
    [InlineData(SharePermission.None, SecretPermission.None)]
    public async Task ForSecretAsync_DirectGrantNoFolderGrants_ReturnsItWithoutReadingTheSecret(SharePermission granted, SecretPermission expected)
    {
        _world.DirectGrant(_secretId, granted);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(expected, result);
        Assert.Equal(0, _world.Reads("GetSecretAsync"));
        Assert.Equal(0, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_DirectGrantSatisfiesStopAt_DoesNotReadTheSecretEvenWithFolderGrants()
    {
        var folders = _world.Chain(2);
        _world.FolderGrant(folders[1], SharePermission.View);
        _world.DirectGrant(_secretId, SharePermission.Edit);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Edit, Token);

        Assert.Equal(SecretPermission.Edit, result);
        Assert.Equal(0, _world.Reads("GetSecretAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_DirectGrantSecretBinned_FolderGrantsExist_ReturnsNone()
    {
        var folders = _world.Chain(1);
        _world.FolderGrant(folders[0], SharePermission.View);
        _world.DirectGrant(_secretId, SharePermission.View);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.None, result);
    }

    [Fact]
    public async Task ForSecretAsync_FolderGrantOnDirectParent_ReturnsItWithOneFolderRead()
    {
        var folders = _world.Chain(3);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[0], SharePermission.Edit);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Edit, Token);

        Assert.Equal(SecretPermission.Edit, result);
        Assert.Equal(1, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_FolderGrantOnGrandparent_WalksUp()
    {
        var folders = _world.Chain(4);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[2], SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.Equal(SecretPermission.Manage, result);
        Assert.Equal(3, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_FolderGrantButSecretNotInChain_ReturnsNone()
    {
        var folders = _world.Chain(2);
        var unrelated = _world.Chain(2);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(unrelated[0], SharePermission.Manage);
        _world.FolderGrant(unrelated[1], SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.Equal(SecretPermission.None, result);
    }

    [Fact]
    public async Task ForSecretAsync_SecretAtVaultRootWithFolderGrants_ReturnsNoneWithoutFolderReads()
    {
        var folders = _world.Chain(1);
        _world.ActiveSecret(_secretId, null);
        _world.FolderGrant(folders[0], SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.Equal(SecretPermission.None, result);
        Assert.Equal(0, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_MissingFolderInChain_StopsAndKeepsBest()
    {
        var folders = _world.Chain(3);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.Store.GetFolderAsync(_world.OwnerId, folders[1], Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<Folder>?>(null));
        _world.DirectGrant(_secretId, SharePermission.View);
        _world.FolderGrant(folders[2], SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.View, result);
        Assert.Equal(2, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_StopAtManage_ReadsWholeChain()
    {
        var folders = _world.Chain(4);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[0], SharePermission.Edit);
        _world.FolderGrant(folders[3], SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.Manage, result);
        Assert.Equal(4, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_StopAtView_ReadsNoMoreThanNeeded()
    {
        var folders = _world.Chain(4);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[0], SharePermission.View);
        _world.FolderGrant(folders[3], SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        Assert.Equal(SecretPermission.View, result);
        Assert.Equal(1, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_DirectViewFolderEdit_IsEdit()
    {
        var folders = _world.Chain(2);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.DirectGrant(_secretId, SharePermission.View);
        _world.FolderGrant(folders[1], SharePermission.Edit);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.Edit, result);
    }

    [Fact]
    public async Task ForSecretAsync_DirectManageFolderView_IsManage()
    {
        var folders = _world.Chain(2);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.DirectGrant(_secretId, SharePermission.Manage);
        _world.FolderGrant(folders[1], SharePermission.View);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Owner, Token);

        Assert.Equal(SecretPermission.Manage, result);
    }

    [Fact]
    public async Task ForSecretAsync_ChildViewParentManage_IsManage()
    {
        var folders = _world.Chain(2);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[0], SharePermission.View);
        _world.FolderGrant(folders[1], SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.Manage, result);
    }

    [Fact]
    public async Task ForSecretAsync_ChildManageParentView_IsManage()
    {
        var folders = _world.Chain(2);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[0], SharePermission.Manage);
        _world.FolderGrant(folders[1], SharePermission.View);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Owner, Token);

        Assert.Equal(SecretPermission.Manage, result);
    }

    [Theory]
    [InlineData(SharePermission.View, SharePermission.Manage)]
    [InlineData(SharePermission.Manage, SharePermission.View)]
    public async Task ForSecretAsync_TwoGrantsOnSameFolder_TakesTheHigher(SharePermission first, SharePermission second)
    {
        var folders = _world.Chain(1);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[0], first);
        _world.FolderGrant(folders[0], second);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Owner, Token);

        Assert.Equal(SecretPermission.Manage, result);
    }

    [Fact]
    public async Task ForSecretAsync_CycleInStoredFolders_StopsAndDeniesBeyondChain()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _world.ActiveFolder(first, second);
        _world.ActiveFolder(second, first);
        _world.ActiveSecret(_secretId, first);
        _world.FolderGrant(Guid.NewGuid(), SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.None, result);
        Assert.Equal(VaultLimits.MaxFolderDepth, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_ChainOfTen_MakesAtMostTenFolderReads()
    {
        var folders = _world.Chain(VaultLimits.MaxFolderDepth);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[^1], SharePermission.View);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.View, result);
        Assert.Equal(VaultLimits.MaxFolderDepth, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_GrantBeyondDepthLimit_IsNotReached()
    {
        var folders = _world.Chain(VaultLimits.MaxFolderDepth + 1);
        _world.ActiveSecret(_secretId, folders[0]);
        _world.FolderGrant(folders[^1], SharePermission.Manage);

        var result = await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.None, result);
        Assert.Equal(VaultLimits.MaxFolderDepth, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForSecretAsync_AnyCall_QueriesGrantsOfTheCallerOnly()
    {
        await NewResolver().ForSecretAsync(_world.OwnerId, _secretId, SecretPermission.View, Token);

        await _world.Store.Received(1).GetShareGrantAsync(_world.OwnerId, ShareItemKind.Secret, _secretId, _caller.UserId, Token);
        await _world.Store.Received(1).ListFolderGrantsToRecipientAsync(_world.OwnerId, _caller.UserId, Token);
    }

    [Fact]
    public async Task ForFolderAsync_NoFolderGrants_ReturnsNoneWithOneRead()
    {
        var result = await NewResolver().ForFolderAsync(_world.OwnerId, Guid.NewGuid(), SecretPermission.View, Token);

        Assert.Equal(SecretPermission.None, result);
        Assert.Equal(1, _world.TotalReads);
    }

    [Fact]
    public async Task ForFolderAsync_GrantOnAncestor_ReturnsIt()
    {
        var folders = _world.Chain(3);
        _world.FolderGrant(folders[2], SharePermission.Edit);

        var result = await NewResolver().ForFolderAsync(_world.OwnerId, folders[0], SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.Edit, result);
        Assert.Equal(3, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForFolderAsync_GrantOnChildOnly_ReturnsNone()
    {
        var folders = _world.Chain(3);
        _world.FolderGrant(folders[0], SharePermission.Manage);

        var result = await NewResolver().ForFolderAsync(_world.OwnerId, folders[2], SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.None, result);
    }

    [Fact]
    public async Task ForFolderAsync_MissingFolder_ReturnsNone()
    {
        _world.FolderGrant(Guid.NewGuid(), SharePermission.Manage);

        var result = await NewResolver().ForFolderAsync(_world.OwnerId, Guid.NewGuid(), SecretPermission.View, Token);

        Assert.Equal(SecretPermission.None, result);
    }

    [Fact]
    public async Task ForFolderAsync_GrantBeyondDepthLimit_IsNotReached()
    {
        var folders = _world.Chain(VaultLimits.MaxFolderDepth + 1);
        _world.FolderGrant(folders[^1], SharePermission.Manage);

        var result = await NewResolver().ForFolderAsync(_world.OwnerId, folders[0], SecretPermission.Manage, Token);

        Assert.Equal(SecretPermission.None, result);
        Assert.Equal(VaultLimits.MaxFolderDepth, _world.Reads("GetFolderAsync"));
    }

    [Fact]
    public async Task ForFolderAsync_StopAtView_StopsAtFirstSatisfyingFolder()
    {
        var folders = _world.Chain(4);
        _world.FolderGrant(folders[1], SharePermission.View);
        _world.FolderGrant(folders[3], SharePermission.Manage);

        var result = await NewResolver().ForFolderAsync(_world.OwnerId, folders[0], SecretPermission.View, Token);

        Assert.Equal(SecretPermission.View, result);
        Assert.Equal(2, _world.Reads("GetFolderAsync"));
    }

    private ShareAccessResolver NewResolver() => new(_world.Store, _caller);
}
