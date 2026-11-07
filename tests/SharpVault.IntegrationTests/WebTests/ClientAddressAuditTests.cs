// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Transfers;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebApiTestGroup))]
[Trait("Category", "Security")]
public sealed class ClientAddressAuditTests : IAsyncLifetime
{
    private const string SecretValue = "client-address-audit-value-4a1f";
    private const int JobRunBound = 60;
    private const string Reason = "Role change, the vault moves to the new owner";

    private readonly ApiTestFactory _api;
    private readonly WebTestFactory _web;

    public ClientAddressAuditTests(ApiTestFactory api, WebTestFactory web)
    {
        _api = api;
        _web = web;
        _web.UseApi(api);
    }

    public ValueTask InitializeAsync()
    {
        _api.Logs.Clear();
        _web.Logs.Clear();
        _api.StorageRequests.Clear();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _api.ResetStorageAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    public static TheoryData<AuditEventType> AuditedFamilies => new()
    {
        { AuditEventType.SecretValueRevealed },
        { AuditEventType.SecretShared },
        { AuditEventType.UserLocked },
        { AuditEventType.UserUnlocked },
        { AuditEventType.TransferRequested },
    };

    [Theory]
    [MemberData(nameof(AuditedFamilies))]
    public async Task ClientAddress_EveryAuditedFamilyThroughTheWeb_RowHoldsBrowserAddressNotWebHost(AuditEventType eventType)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAllAsync(cancellationToken);
        using var user = WebCircuit.For(_web, TestUsers.User, TestAddresses.Browser);
        using var admin = WebCircuit.For(_web, TestUsers.Admin, TestAddresses.Browser);
        await PerformAsync(eventType, user, admin, cancellationToken);

        var rows = await AuditRows.ListAsync(_api.StorageAdminClient.GetTableClient(TableNames.AuditLog), cancellationToken);
        var row = Assert.Single(rows, candidate => candidate.GetString("EventType") == eventType.ToString());
        Assert.Equal(TestAddresses.Browser, row.GetString("IpAddress"));
        Assert.NotEqual(TestAddresses.WebHost.ToString(), row.GetString("IpAddress"));
    }

    [Fact]
    public async Task ClientAddress_TransferApprovedThroughTheWeb_JobAndRowHoldBrowserAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAllAsync(cancellationToken);
        using var admin = WebCircuit.For(_web, TestUsers.Admin, TestAddresses.Browser);
        using var secondAdmin = WebCircuit.For(_web, TestUsers.SecondAdmin, TestAddresses.Browser);
        var transfer = Succeeded(await admin.AdminTransfers.CreateAsync(
            new CreateTransferRequestDto
            {
                SourceUserId = TestUsers.User.ObjectId,
                TargetUserId = TestUsers.OtherUser.ObjectId,
                Scope = TransferScopeKindDto.AllItems,
                Category = ReasonCategoryDto.RoleChange,
                Reason = Reason,
            },
            cancellationToken));

        var approved = await secondAdmin.AdminTransfers.ApproveAsync(transfer.Id, transfer.ConcurrencyToken, cancellationToken);

        Assert.Equal(ApiCallStatus.Success, approved.Status);
        var runner = _api.Services.GetRequiredService<ITransferJobRunner>();
        var rows = await AuditRows.ListAsync(_api.StorageAdminClient.GetTableClient(TableNames.AuditLog), cancellationToken);
        for (var attempt = 0; attempt < JobRunBound && !rows.Any(IsTransferApproved); attempt++)
        {
            await runner.RunOnceAsync(cancellationToken);
            rows = await AuditRows.ListAsync(_api.StorageAdminClient.GetTableClient(TableNames.AuditLog), cancellationToken);
        }

        var row = Assert.Single(rows, IsTransferApproved);
        Assert.Equal(TestAddresses.Browser, row.GetString("IpAddress"));
        var job = await _api.StorageAdminClient
            .GetTableClient(TableNames.TransferJobs)
            .GetEntityAsync<TableEntity>(TransferKeys.TransferPartition, TransferKeys.JobRowKey(transfer.Id), cancellationToken: cancellationToken);
        Assert.Equal(TestAddresses.Browser, job.Value.GetString("ApprovedFromIp"));
    }

    [Fact]
    public async Task ClientAddress_WebKnowsNoAddress_ActionSucceedsAndRowAddressIsNull()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await WarmUpAllAsync(cancellationToken);
        using var user = WebCircuit.For(_web, TestUsers.User);
        var secret = Succeeded(await user.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "No address", Value = SecretValue },
            cancellationToken));

        var revealed = await user.VaultSecrets.RevealSecretAsync(
            secret.Id,
            new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View },
            cancellationToken);

        Assert.Equal(ApiCallStatus.Success, revealed.Status);
        var rows = await AuditRows.ListAsync(_api.StorageAdminClient.GetTableClient(TableNames.AuditLog), cancellationToken);
        var row = Assert.Single(rows, candidate => candidate.GetString("EventType") == nameof(AuditEventType.SecretValueRevealed));
        Assert.Null(row.GetString("IpAddress"));
    }

    private static bool IsTransferApproved(TableEntity row) => row.GetString("EventType") == nameof(AuditEventType.TransferApproved);

    private async Task WarmUpAllAsync(CancellationToken cancellationToken)
    {
        foreach (var user in new[] { TestUsers.User, TestUsers.OtherUser, TestUsers.Admin, TestUsers.SecondAdmin })
        {
            using var client = _api.CreateAuthenticatedClient(user);
            using var response = await client.GetAsync("/api/me", cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task PerformAsync(AuditEventType eventType, WebCircuit user, WebCircuit admin, CancellationToken cancellationToken)
    {
        switch (eventType)
        {
            case AuditEventType.SecretValueRevealed:
                await RevealAsync(user, cancellationToken);
                break;
            case AuditEventType.SecretShared:
                await ShareAsync(user, cancellationToken);
                break;
            case AuditEventType.UserLocked:
                Succeeded(await admin.AdminUsers.LockAsync(TestUsers.User.ObjectId, LockRequest(), cancellationToken));
                break;
            case AuditEventType.UserUnlocked:
                Succeeded(await admin.AdminUsers.LockAsync(TestUsers.User.ObjectId, LockRequest(), cancellationToken));
                Succeeded(await admin.AdminUsers.UnlockAsync(TestUsers.User.ObjectId, LockRequest(), cancellationToken));
                break;
            case AuditEventType.TransferRequested:
                Succeeded(await admin.AdminTransfers.CreateAsync(
                    new CreateTransferRequestDto
                    {
                        SourceUserId = TestUsers.User.ObjectId,
                        TargetUserId = TestUsers.OtherUser.ObjectId,
                        Scope = TransferScopeKindDto.AllItems,
                        Category = ReasonCategoryDto.RoleChange,
                        Reason = Reason,
                    },
                    cancellationToken));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "The family has no action.");
        }
    }

    private static async Task RevealAsync(WebCircuit user, CancellationToken cancellationToken)
    {
        var secret = Succeeded(await user.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Revealed", Value = SecretValue },
            cancellationToken));
        Succeeded(await user.VaultSecrets.RevealSecretAsync(
            secret.Id,
            new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View },
            cancellationToken));
    }

    private static async Task ShareAsync(WebCircuit user, CancellationToken cancellationToken)
    {
        var secret = Succeeded(await user.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Shared", Value = SecretValue },
            cancellationToken));
        Succeeded(await user.ShareGrants.CreateSecretGrantAsync(
            TestUsers.User.ObjectId,
            secret.Id,
            new CreateShareGrantRequestDto { RecipientId = TestUsers.OtherUser.ObjectId, Permission = SharePermissionDto.View },
            cancellationToken));
    }

    private static LockUserRequestDto LockRequest() => new()
    {
        Category = ReasonCategoryDto.Other,
        Reason = Reason,
    };

    private static T Succeeded<T>(ApiResponse<T> response)
    {
        Assert.Equal(ApiCallStatus.Success, response.Status);
        return response.Value!;
    }
}
