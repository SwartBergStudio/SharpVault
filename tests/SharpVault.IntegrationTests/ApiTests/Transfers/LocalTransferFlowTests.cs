// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Authentication.Local;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalTransferFlowTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private const int RunBound = 60;

    private const string ValidReason = "Role change, the vault moves to the new owner";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ITransferJobRunner Runner => factory.Services.GetRequiredService<ITransferJobRunner>();

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(CancellationToken.None);
        factory.StorageRequests.Clear();
        factory.Logs.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
        return ValueTask.CompletedTask;
    }

    private VaultTestApi ApiFor(LocalAccount account) => new(factory.CreateClientForAccount(account));

    private static CreateTransferRequestDto CreateBody(LocalAccount source, LocalAccount target) => new()
    {
        SourceUserId = source.Id,
        TargetUserId = target.Id,
        Scope = TransferScopeKindDto.AllItems,
        Category = ReasonCategoryDto.RoleChange,
        Reason = ValidReason,
    };

    private async Task<TransferDto> RequestAsync(LocalAccount requester, LocalAccount source, LocalAccount target)
    {
        var (response, body) = await ApiFor(requester).SendAsync<TransferDto>(HttpMethod.Post, ApiRoutes.AdminTransfers, CreateBody(source, target));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return body ?? throw new InvalidOperationException("The create response had no body.");
    }

    private async Task<(HttpResponseMessage Response, TransferDto? Body)> ApproveRawAsync(LocalAccount approver, TransferDto transfer) =>
        await ApiFor(approver).SendAsync<TransferDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferApprove, ("transferId", transfer.Id)),
            body: null,
            ifMatch: transfer.ConcurrencyToken);

    private async Task<TransferDto> GetTransferAsync(LocalAccount viewer, Guid transferId)
    {
        var (response, body) = await ApiFor(viewer).SendAsync<TransferDto>(HttpMethod.Get, VaultTestApi.Bind(ApiRoutes.AdminTransfer, ("transferId", transferId)));
        Assert.True(response.IsSuccessStatusCode, $"Get failed with {response.StatusCode}.");
        return body ?? throw new InvalidOperationException("The get response had no body.");
    }

    private async Task<TransferDto> RunUntilCompletedAsync(LocalAccount viewer, Guid transferId)
    {
        var current = await GetTransferAsync(viewer, transferId);
        for (var attempt = 0; attempt < RunBound && current.State != TransferStateDto.Completed; attempt++)
        {
            await Runner.RunOnceAsync(Token);
            current = await GetTransferAsync(viewer, transferId);
        }

        return current;
    }

    private async Task<List<(Guid Id, string Value)>> SeedSecretsAsync(LocalAccount owner, int count)
    {
        var api = ApiFor(owner);
        var seeded = new List<(Guid Id, string Value)>();
        for (var index = 0; index < count; index++)
        {
            var value = $"local-transfer-value-{Guid.NewGuid():N}";
            var (response, body) = await api.CreateSecretAsync(new CreateSecretRequestDto
            {
                Type = SecretTypeDto.Login,
                Name = $"Transfer {index} {Guid.NewGuid():N}",
                Value = value,
            });
            Assert.True(response.IsSuccessStatusCode, $"Seeding failed with {response.StatusCode}.");
            seeded.Add((body!.Id, value));
        }

        return seeded;
    }

    private async Task<List<SecretDto>> ListAllAsync(LocalAccount owner)
    {
        var api = ApiFor(owner);
        var all = new List<SecretDto>();
        string? token = null;
        do
        {
            var (response, page) = await api.ListSecretsAsync(pageSize: 50, continuationToken: token);
            Assert.True(response.IsSuccessStatusCode, $"List failed with {response.StatusCode}.");
            all.AddRange(page?.Items ?? []);
            token = page?.ContinuationToken;
        }
        while (token is not null);
        return all;
    }

    [Fact]
    public async Task LocalTransfer_AdminOneRequests_AdminOneCannotApprove_AdminTwoApproves_UserTwoReceives()
    {
        var seeded = await SeedSecretsAsync(LocalAccounts.UserOne, 3);
        var transfer = await RequestAsync(LocalAccounts.AdminOne, LocalAccounts.UserOne, LocalAccounts.UserTwo);

        var (selfApprove, _) = await ApproveRawAsync(LocalAccounts.AdminOne, transfer);
        Assert.Equal(HttpStatusCode.Forbidden, selfApprove.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(LocalAccounts.AdminOne, transfer.Id)).State);

        var (approve, approved) = await ApproveRawAsync(LocalAccounts.AdminTwo, transfer);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.NotNull(approved);

        var settled = await RunUntilCompletedAsync(LocalAccounts.AdminTwo, transfer.Id);
        Assert.Equal(TransferStateDto.Completed, settled.State);

        var received = await ListAllAsync(LocalAccounts.UserTwo);
        Assert.Equal(seeded.Count, received.Count);
        foreach (var (id, value) in seeded)
        {
            var (response, revealed) = await ApiFor(LocalAccounts.UserTwo).RevealSecretAsync(
                id,
                new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
            Assert.True(response.IsSuccessStatusCode, $"Reveal failed with {response.StatusCode}.");
            Assert.Equal(value, revealed?.Value);
        }

        Assert.Empty(await ListAllAsync(LocalAccounts.UserOne));
        foreach (var (id, _) in seeded)
        {
            var (response, _) = await ApiFor(LocalAccounts.UserOne).GetSecretAsync(id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task LocalTransfer_PublishedItemCount_CountsOnlyTheSecretsMoved()
    {
        var seeded = await SeedSecretsAsync(LocalAccounts.UserOne, 3);
        var transfer = await RequestAsync(LocalAccounts.AdminOne, LocalAccounts.UserOne, LocalAccounts.UserTwo);
        await ApproveRawAsync(LocalAccounts.AdminTwo, transfer);

        var settled = await RunUntilCompletedAsync(LocalAccounts.AdminTwo, transfer.Id);

        Assert.Equal(seeded.Count, settled.PublishedItemCount);
    }

    [Fact]
    public async Task LocalTransfer_UserTwoSeesNothingBeforeCompletion()
    {
        await SeedSecretsAsync(LocalAccounts.UserOne, 2);
        var transfer = await RequestAsync(LocalAccounts.AdminOne, LocalAccounts.UserOne, LocalAccounts.UserTwo);

        Assert.Empty(await ListAllAsync(LocalAccounts.UserTwo));

        var (approve, _) = await ApproveRawAsync(LocalAccounts.AdminTwo, transfer);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Empty(await ListAllAsync(LocalAccounts.UserTwo));
        Assert.NotEqual(TransferStateDto.Completed, (await GetTransferAsync(LocalAccounts.AdminTwo, transfer.Id)).State);

        var settled = await RunUntilCompletedAsync(LocalAccounts.AdminTwo, transfer.Id);
        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Equal(2, (await ListAllAsync(LocalAccounts.UserTwo)).Count);
    }

    [Fact]
    public async Task LocalTransfer_AdminsNeverReadTheItems()
    {
        var seeded = await SeedSecretsAsync(LocalAccounts.UserOne, 1);
        var transfer = await RequestAsync(LocalAccounts.AdminOne, LocalAccounts.UserOne, LocalAccounts.UserTwo);
        await ApproveRawAsync(LocalAccounts.AdminTwo, transfer);
        await RunUntilCompletedAsync(LocalAccounts.AdminTwo, transfer.Id);

        foreach (var admin in new[] { LocalAccounts.AdminOne, LocalAccounts.AdminTwo })
        {
            Assert.Empty(await ListAllAsync(admin));
            var (response, _) = await ApiFor(admin).GetSecretAsync(seeded[0].Id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task LocalTransfer_UserOneCannotCreateOrApprove()
    {
        var transfer = await RequestAsync(LocalAccounts.AdminOne, LocalAccounts.UserOne, LocalAccounts.UserTwo);

        var (create, _) = await ApiFor(LocalAccounts.UserOne).SendAsync<TransferDto>(HttpMethod.Post, ApiRoutes.AdminTransfers, CreateBody(LocalAccounts.UserOne, LocalAccounts.UserTwo));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        var (approve, _) = await ApproveRawAsync(LocalAccounts.UserTwo, transfer);
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.Equal(TransferStateDto.PendingApproval, (await GetTransferAsync(LocalAccounts.AdminOne, transfer.Id)).State);
    }
}
