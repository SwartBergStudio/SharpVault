// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Reflection;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Users;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Audit;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Transfers;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.ApiTests;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.ApiTests.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

public abstract class TransferTestBase(ApiTestFactory factory) : VaultTestBase(factory)
{
    protected const string ValidReason = "Role change, the vault moves to the new owner";

    protected const int RunBound = 60;

    protected const string UserRole = "SharpVault.User";

    private readonly List<WebApplicationFactory<Program>> _ownedHosts = [];

    private WebApplicationFactory<Program>? _clockedHost;

    protected WebApplicationFactory<Program> Host => _clockedHost ?? Factory;

    protected ITransferJobRunner Runner => Host.Services.GetRequiredService<ITransferJobRunner>();

    protected static TestUser WithoutRoles(TestUser user) => user with { Roles = [] };

    protected void UseHost(WebApplicationFactory<Program> host)
    {
        _ownedHosts.Add(host);
        _clockedHost = host;
    }

    protected void UseFakeClock(FakeTimeProvider clock) =>
        UseHost(CreateTimedFactory(clock, TimeSpan.Zero));

    public override async ValueTask DisposeAsync()
    {
        foreach (var host in _ownedHosts)
        {
            await host.DisposeAsync();
        }

        _ownedHosts.Clear();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    protected WebApplicationFactory<Program> RaceHost(
        FakeTimeProvider clock,
        Func<MethodInfo, object?[]?, Task>? vaultHook,
        Func<AuditEntry, Task>? auditHook) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            if (vaultHook is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IVaultStore>(sp =>
                    HookProxy.Wrap<IVaultStore>(ActivatorUtilities.CreateInstance<TableVaultStore>(sp), vaultHook)));
            }

            if (auditHook is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IAuditLog>(sp =>
                    HookProxy.Wrap<IAuditLog>(
                        ActivatorUtilities.CreateInstance<TableAuditLog>(sp),
                        (_, args) => args is [AuditEntry entry, ..] ? auditHook(entry) : Task.CompletedTask)));
            }
        }));

    protected static bool IsReleasePack(MethodInfo method, object?[]? args) =>
        method.Name == nameof(IVaultStore.SubmitAsync)
        && args?[1] is IEnumerable<VaultChange> changes
        && changes.Any(change => change.Kind is VaultChangeKind.RemoveSecret or VaultChangeKind.RemoveRecycledSecret);

    protected static Func<MethodInfo, object?[]?, Task> BeforeFirstReleasePack(Func<Task> action)
    {
        var fired = 0;
        return async (method, args) =>
        {
            if (IsReleasePack(method, args) && Interlocked.Exchange(ref fired, 1) == 0)
            {
                await action();
            }
        };
    }

    protected VaultTestApi ApiAs(TestUser user) => new(CreateClientFor(Host, TestTokens.Create(user)));

    protected async Task WarmUpUserAsync(TestUser user)
    {
        using var client = CreateClientFor(Host, TestTokens.Create(user));
        var response = await client.GetAsync("/api/me", Token);
        response.EnsureSuccessStatusCode();
    }

    protected async Task WarmUpAllAsync(params TestUser[] users)
    {
        foreach (var user in users)
        {
            await WarmUpUserAsync(user);
        }
    }

    protected static CreateTransferRequestDto CreateBody(
        TestUser source,
        TestUser target,
        ReasonCategoryDto category = ReasonCategoryDto.RoleChange,
        TransferScopeKindDto scope = TransferScopeKindDto.AllItems,
        List<Guid>? folderIds = null,
        string reason = ValidReason) => new()
    {
        SourceUserId = source.ObjectId,
        TargetUserId = target.ObjectId,
        Scope = scope,
        FolderIds = folderIds,
        Category = category,
        Reason = reason,
    };

    protected static string CreateJsonBody(TestUser source, TestUser target, string extraFields = "") =>
        $"{{\"sourceUserId\":\"{source.ObjectId:D}\",\"targetUserId\":\"{target.ObjectId:D}\",\"scope\":\"{nameof(TransferScopeKindDto.AllItems)}\",\"category\":\"{nameof(ReasonCategoryDto.RoleChange)}\",\"reason\":\"{ValidReason}\"{extraFields}}}";

    protected async Task<(HttpResponseMessage Response, TransferDto? Body)> CreateRawAsync(TestUser requester, CreateTransferRequestDto body) =>
        await ApiAs(requester).SendAsync<TransferDto>(HttpMethod.Post, ApiRoutes.AdminTransfers, body);

    protected async Task<TransferDto> RequestAsync(
        TestUser requester,
        TestUser source,
        TestUser target,
        ReasonCategoryDto category = ReasonCategoryDto.RoleChange,
        TransferScopeKindDto scope = TransferScopeKindDto.AllItems,
        List<Guid>? folderIds = null,
        string reason = ValidReason)
    {
        var (response, transfer) = await CreateRawAsync(requester, CreateBody(source, target, category, scope, folderIds, reason));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return transfer ?? throw new InvalidOperationException("The create response had no body.");
    }

    protected async Task<(HttpResponseMessage Response, TransferDto? Body)> ApproveRawAsync(TestUser approver, Guid transferId, string? ifMatch) =>
        await ApiAs(approver).SendAsync<TransferDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferApprove, ("transferId", transferId)),
            body: null,
            ifMatch: ifMatch);

    protected async Task<TransferDto> ApproveAsync(TestUser approver, TransferDto transfer)
    {
        var (response, body) = await ApproveRawAsync(approver, transfer.Id, transfer.ConcurrencyToken);
        Assert.True(response.IsSuccessStatusCode, $"Approve failed with {response.StatusCode}.");
        return body ?? throw new InvalidOperationException("The approve response had no body.");
    }

    protected async Task<(HttpResponseMessage Response, TransferDto? Body)> GetRawAsync(TestUser viewer, Guid transferId) =>
        await ApiAs(viewer).SendAsync<TransferDto>(HttpMethod.Get, VaultTestApi.Bind(ApiRoutes.AdminTransfer, ("transferId", transferId)));

    protected async Task<TransferDto> GetTransferAsync(TestUser viewer, Guid transferId)
    {
        var (response, body) = await GetRawAsync(viewer, transferId);
        Assert.True(response.IsSuccessStatusCode, $"Get failed with {response.StatusCode}.");
        return body ?? throw new InvalidOperationException("The get response had no body.");
    }

    protected Task<TransferDto> RunUntilSettledAsync(TestUser viewer, Guid transferId, TransferStateDto settledState) =>
        RunUntilAsync(viewer, transferId, transfer => transfer.State == settledState);

    protected async Task<TransferDto> RunUntilAsync(TestUser viewer, Guid transferId, Func<TransferDto, bool> isDone)
    {
        var current = await GetTransferAsync(viewer, transferId);
        for (var attempt = 0; attempt < RunBound && !isDone(current); attempt++)
        {
            await Runner.RunOnceAsync(Token);
            current = await GetTransferAsync(viewer, transferId);
        }

        return current;
    }

    protected WebApplicationFactory<Program> CrashingTransferHost(CrashPlan plan, FakeTimeProvider clock) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Replace(ServiceDescriptor.Singleton<IVaultStore>(sp =>
                CrashProxies.Wrap<IVaultStore>(ActivatorUtilities.CreateInstance<TableVaultStore>(sp), plan)));
            services.Replace(ServiceDescriptor.Singleton<ITransferStagingStore>(sp =>
                CrashProxies.Wrap<ITransferStagingStore>(ActivatorUtilities.CreateInstance<TableTransferStagingStore>(sp), plan)));
            services.Replace(ServiceDescriptor.Singleton<ITransferJobStore>(sp =>
                CrashProxies.Wrap<ITransferJobStore>(ActivatorUtilities.CreateInstance<TableTransferJobStore>(sp), plan)));
        }));

    protected async Task<int> StagedRowCountAsync(Guid owner, Guid transferId)
    {
        var lower = VaultKeys.StagingRangeStart(transferId);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {VaultKeys.PartitionKey(owner)} and RowKey ge {lower} and RowKey lt {VaultKeys.UpperBound(lower)}");
        var count = 0;
        await foreach (var _ in VaultsTable.QueryAsync<TableEntity>(filter, select: ["PartitionKey"], cancellationToken: Token))
        {
            count++;
        }

        return count;
    }

    protected async Task SetUserStatusAsync(Guid userId, UserStatus status)
    {
        using var scope = Host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var stored = await users.GetAsync(userId, Token) ?? throw new InvalidOperationException("The user row is missing.");
        stored.Value.Status = status;
        var result = await users.ReplaceAsync(stored.Value, stored.ETag, Token);
        Assert.Equal(StoreWriteResult.Written, result);
    }

    protected async Task<int> CountAuditEventsAsync(AuditEventType eventType, Guid targetId)
    {
        var rows = await AuditRows.ListAsync(GetAuditTable(), Token);
        return rows.Count(row => row.GetString("EventType") == eventType.ToString()
            && row.GetString("TargetId") == targetId.ToString());
    }

    protected async Task<int> VaultRowCountAsync(Guid owner)
    {
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {VaultKeys.PartitionKey(owner)}");
        var count = 0;
        await foreach (var _ in VaultsTable.QueryAsync<TableEntity>(filter, select: ["PartitionKey"], cancellationToken: Token))
        {
            count++;
        }

        return count;
    }

    protected async Task<SeededVault> SeedVaultAsync(TestUser owner, int secretCount, int folderCount) =>
        await VaultSeeder.SeedAsync(Factory, owner.ObjectId, secretCount, folderCount, Token);

    protected async Task<IReadOnlyList<SecretDto>> ListAllSecretsAsync(TestUser owner)
    {
        var api = ApiAs(owner);
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

    protected async Task<string> RevealAsync(TestUser viewer, Guid secretId, RevealFieldDto field)
    {
        var (response, body) = await ApiAs(viewer).RevealSecretAsync(secretId, new RevealSecretRequestDto { Field = field, Intent = RevealIntentDto.View });
        Assert.True(response.IsSuccessStatusCode, $"Reveal failed with {response.StatusCode}.");
        return body?.Value ?? string.Empty;
    }
}
