// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.Infrastructure.Audit;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkLockFailureTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private const string LockReason = "Left the company, links end with the employment";

    private static readonly string LeftCompanyJson = $"{{\"category\":\"{nameof(ReasonCategoryDto.LeftCompany)}\",\"reason\":\"{LockReason}\"}}";

    [Fact]
    public async Task Lock_RepeatAfterFailedSweep_FinishesForANonDismissedCategory()
    {
        var secret = await NewSecretAsync();
        var first = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var second = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var plan = new AuditEventCrashPlan(AuditEventType.OneTimeLinkRevoked);
        using (var crashing = AuditCrashHost(plan))
        {
            plan.Arm();
            using var failed = await PostLockAsync(crashing, LeftCompanyJson);

            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }

        Assert.True(plan.Fired);
        var survivor = await SurvivingLinkAsync(first, second);
        using var repeat = await PostLockAsync(Factory, LeftCompanyJson);

        Assert.Equal(HttpStatusCode.BadRequest, repeat.StatusCode);
        Assert.Null(await Factory.Services.GetRequiredService<IOneTimeLinkStore>().GetAsync(survivor, Token));
        using (var opened = await OpenAsync(survivor))
        {
            Assert.Equal(HttpStatusCode.NotFound, opened.StatusCode);
        }

        var revoked = Assert.Single(await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkRevoked, survivor));
        Assert.Equal(TestUsers.Admin.ObjectId.ToString(), revoked.GetString("ActorId"));
    }

    [Fact]
    public async Task Unlock_AfterPartialSweep_LinkDoesNotOpen()
    {
        var secret = await NewSecretAsync();
        var first = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var second = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var plan = new AuditEventCrashPlan(AuditEventType.OneTimeLinkRevoked);
        using (var crashing = AuditCrashHost(plan))
        {
            plan.Arm();
            using var failed = await PostLockAsync(crashing, LeftCompanyJson);

            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }

        Assert.True(plan.Fired);
        var survivor = await SurvivingLinkAsync(first, second);
        using (var unlocked = await PostAdminAsync(ApiRoutes.AdminUserUnlock, LeftCompanyJson))
        {
            Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);
        }

        using var opened = await OpenAsync(survivor);
        Assert.Equal(HttpStatusCode.NotFound, opened.StatusCode);
        Assert.Single(await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkRevoked, survivor));
    }

    private async Task<Guid> SurvivingLinkAsync(Guid first, Guid second)
    {
        var store = Factory.Services.GetRequiredService<IOneTimeLinkStore>();
        var survivors = new List<Guid>();
        foreach (var linkId in new[] { first, second })
        {
            if (await store.GetAsync(linkId, Token) is not null)
            {
                survivors.Add(linkId);
            }
        }

        return Assert.Single(survivors);
    }

    private WebApplicationFactory<Program> AuditCrashHost(AuditEventCrashPlan plan) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IAuditLog>(sp =>
                HookProxy.Wrap<IAuditLog>(
                    ActivatorUtilities.CreateInstance<TableAuditLog>(sp),
                    (_, args) => plan.BeforeAppendAsync(args))))));

    private static async Task<HttpResponseMessage> PostLockAsync(WebApplicationFactory<Program> host, string json)
    {
        using var client = CreateClientFor(host, TestTokens.Create(TestUsers.Admin));
        return await SendAdminAsync(client, ApiRoutes.AdminUserLock, json);
    }

    private async Task<HttpResponseMessage> PostAdminAsync(string route, string json)
    {
        using var client = ClientFor(TestUsers.Admin);
        return await SendAdminAsync(client, route, json);
    }

    private static async Task<HttpResponseMessage> SendAdminAsync(HttpClient client, string route, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, VaultTestApi.Bind(route, ("userId", OwnerId)))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        return await client.SendAsync(request, Token);
    }
}
