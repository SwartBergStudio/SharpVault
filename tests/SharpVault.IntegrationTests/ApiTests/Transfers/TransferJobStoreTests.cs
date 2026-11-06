// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Transfers;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferJobStoreTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string StaleEtag = "W/\"datetime'2000-01-01T00%3A00%3A00.0000000Z'\"";

    private ITransferJobStore Store => Host.Services.GetRequiredService<ITransferJobStore>();

    private async Task<Versioned<TransferJob>> OpenJobAsync(Guid transferId)
    {
        var stored = await Store.GetAsync(transferId, Token);
        return stored ?? throw new InvalidOperationException("The transfer job row is missing.");
    }

    private async Task<Guid> RequestTransferAsync()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        return transfer.Id;
    }

    [Fact]
    public async Task Replace_StaleETag_ReturnsNullAndKeepsTheRow()
    {
        var transferId = await RequestTransferAsync();
        var current = await OpenJobAsync(transferId);

        var replaced = await Store.ReplaceAsync(current.Value, StaleEtag, Token);

        Assert.Null(replaced);
        Assert.Equal(current.ETag, (await OpenJobAsync(transferId)).ETag);
    }

    [Fact]
    public async Task Replace_CurrentETag_ReturnsANewVersion()
    {
        var transferId = await RequestTransferAsync();
        var current = await OpenJobAsync(transferId);

        var replaced = await Store.ReplaceAsync(current.Value, current.ETag, Token);

        Assert.NotNull(replaced);
        Assert.NotEqual(current.ETag, replaced.ETag);
    }

    [Fact]
    public async Task Close_StaleETag_KeepsTheMarker()
    {
        var transferId = await RequestTransferAsync();
        var current = await OpenJobAsync(transferId);

        var result = await Store.CloseAsync(current.Value, StaleEtag, Token);

        Assert.NotEqual(StoreWriteResult.Written, result);
        var marker = await Store.GetMarkerAsync(TestUsers.User.ObjectId, Token);
        Assert.NotNull(marker);
        Assert.Equal(transferId, marker.TransferId);
    }

    [Fact]
    public async Task Marker_WhileOpen_IsListedAsActiveForItsSource()
    {
        var transferId = await RequestTransferAsync();

        var active = await Store.ListActiveAsync(Token);

        Assert.Contains(active, marker => marker.TransferId == transferId && marker.SourceUserId == TestUsers.User.ObjectId);
    }

    [Fact]
    public async Task Marker_AfterAbort_IsRemovedAndANewRequestIsAllowed()
    {
        var transferId = await RequestTransferAsync();
        var approved = await ApiAs(TestUsers.SecondAdmin).SendRawAsync(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferApprove, ("transferId", transferId)),
            null,
            (await GetTransferAsync(TestUsers.Admin, transferId)).ConcurrencyToken);
        Assert.True(approved.IsSuccessStatusCode, $"Approve failed with {approved.StatusCode}.");
        Assert.NotNull(await Store.GetMarkerAsync(TestUsers.User.ObjectId, Token));

        var aborted = await ApiAs(TestUsers.Admin).SendRawAsync(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transferId)),
            "{\"reason\":\"Aborted after approval, before any data moved\"}",
            (await GetTransferAsync(TestUsers.Admin, transferId)).ConcurrencyToken);

        Assert.True(aborted.IsSuccessStatusCode, $"Abort failed with {aborted.StatusCode}.");
        Assert.Equal(TransferStateDto.Aborted, (await GetTransferAsync(TestUsers.Admin, transferId)).State);
        Assert.NotNull(await Store.GetMarkerAsync(TestUsers.User.ObjectId, Token));
        var (blockedWhileMarked, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser));
        Assert.False(blockedWhileMarked.IsSuccessStatusCode, "A new request must wait until the aborted transfer's staging is gone.");

        await Runner.RunOnceAsync(Token);

        Assert.Null(await Store.GetMarkerAsync(TestUsers.User.ObjectId, Token));
        var (again, _) = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser));
        Assert.True(again.IsSuccessStatusCode, $"Second request failed with {again.StatusCode}.");
    }
}
