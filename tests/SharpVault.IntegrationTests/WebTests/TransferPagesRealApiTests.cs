// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Transfers;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Components.Transfers;
using SharpVault.UI.Shared.Services;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebApiTestGroup))]
public sealed class TransferPagesRealApiTests : IAsyncLifetime
{
    private const string Reason = "Role change, the vault moves to the new owner";

    private readonly ApiTestFactory _api;
    private readonly WebTestFactory _web;

    public TransferPagesRealApiTests(ApiTestFactory api, WebTestFactory web)
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

    private async Task WarmUpAsync(TestUser user)
    {
        using var client = _api.CreateAuthenticatedClient(user);
        using var response = await client.GetAsync("/api/me", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TransferPages_RealApi_AdminListsTheRequestAndNonAdminIsDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.SecondAdmin);
        await WarmUpAsync(TestUsers.User);
        await WarmUpAsync(TestUsers.OtherUser);
        using var admin = WebCircuit.For(_web, TestUsers.Admin);
        using var user = WebCircuit.For(_web, TestUsers.User);

        var created = await admin.AdminTransfers.CreateAsync(
            new CreateTransferRequestDto
            {
                SourceUserId = TestUsers.User.ObjectId,
                TargetUserId = TestUsers.OtherUser.ObjectId,
                Scope = TransferScopeKindDto.AllItems,
                Category = SharpVault.Dtos.Users.ReasonCategoryDto.RoleChange,
                Reason = Reason,
            },
            cancellationToken);
        var listed = await admin.AdminTransfers.ListAsync(TransferList.PageSize, null, cancellationToken);
        var denied = await user.AdminTransfers.ListAsync(TransferList.PageSize, null, cancellationToken);

        Assert.Equal(ApiCallStatus.Success, created.Status);
        Assert.Equal(ApiCallStatus.Success, listed.Status);
        Assert.Contains(listed.Value!.Items, item => item.Id == created.Value!.Id);
        Assert.Equal(ApiCallStatus.AccessDenied, denied.Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TransferPages_RealApi_NoticesLoadForAnAuthenticatedUser()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAsync(TestUsers.OtherUser);
        using var recipient = WebCircuit.For(_web, TestUsers.OtherUser);

        var notices = await recipient.TransferNotices.ListAsync(cancellationToken);

        Assert.Equal(ApiCallStatus.Success, notices.Status);
        Assert.Empty(notices.Value!.Items);
    }
}
