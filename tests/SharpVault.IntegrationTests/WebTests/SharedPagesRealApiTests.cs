// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebApiTestGroup))]
public sealed class SharedPagesRealApiTests : IAsyncLifetime
{
    private readonly ApiTestFactory _api;
    private readonly WebTestFactory _web;

    public SharedPagesRealApiTests(ApiTestFactory api, WebTestFactory web)
    {
        _api = api;
        _web = web;
        _web.UseApi(api);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _api.ResetStorageAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SharedPages_PageSizeConstants_AreAcceptedByTheRealApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = WebCircuit.For(_web, TestUsers.User);
        using var recipient = WebCircuit.For(_web, TestUsers.ShareViewer);
        var folder = Succeeded(await owner.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Shared pages" }, cancellationToken));
        Succeeded(await owner.ShareGrants.CreateFolderGrantAsync(
            TestUsers.User.ObjectId,
            folder.Id,
            new CreateShareGrantRequestDto { RecipientId = TestUsers.ShareViewer.ObjectId, Permission = SharePermissionDto.View },
            cancellationToken));

        var inbox = await recipient.SharedItems.ListAsync(null, SharedWithMeView.PageSize, null, cancellationToken);
        var filed = await recipient.SharedItems.ListAsync(folder.Id, SharedFiledPanel.PageSize, null, cancellationToken);
        var contents = await recipient.SharedItems.GetFolderAsync(TestUsers.User.ObjectId, folder.Id, SharedFolderView.PageSize, null, cancellationToken);

        Assert.Equal(ApiCallStatus.Success, inbox.Status);
        Assert.Contains(inbox.Value!.Items, item => item.ItemId == folder.Id);
        Assert.Equal(ApiCallStatus.Success, filed.Status);
        Assert.Equal(ApiCallStatus.Success, contents.Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task VaultPages_PageSizeConstants_AreAcceptedByTheRealApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);

        var browse = await circuit.VaultSecrets.ListSecretsAsync(null, false, null, SecretListPanel.BrowsePageSize, null, cancellationToken);
        var search = await circuit.VaultSecrets.ListSecretsAsync(null, false, "x", SecretListPanel.SearchPageSize, null, cancellationToken);
        var bin = await circuit.RecycleBin.ListAsync(RecycleBinView.PageSize, null, cancellationToken);

        Assert.Equal(ApiCallStatus.Success, browse.Status);
        Assert.Equal(ApiCallStatus.Success, search.Status);
        Assert.Equal(ApiCallStatus.Success, bin.Status);
    }

    private static T Succeeded<T>(ApiResponse<T> response)
    {
        Assert.Equal(ApiCallStatus.Success, response.Status);
        return response.Value!;
    }
}
