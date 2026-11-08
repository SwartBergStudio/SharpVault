// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkDisabledTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    [Fact]
    public async Task Snapshot_FeatureDisabled_Returns403()
    {
        var secret = await NewSecretAsync();
        using var host = DisabledHost();
        using var client = CreateClientFor(host, TestTokens.Create(Owner));

        using var response = await client.PostAsync(SecretRoute(ApiRoutes.OneTimeLinkSecretSnapshot, OwnerId, secret.Id), null, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_FeatureDisabled_Returns403AndStoresNothing()
    {
        var secret = await NewSecretAsync();
        using var host = DisabledHost();
        using var client = CreateClientFor(host, TestTokens.Create(Owner));

        using var response = await client.PostAsJsonAsync(
            SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, secret.Id),
            new CreateOneTimeLinkRequestDto { Payload = NewPayload(out _) },
            Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var links = await ListAsync(Owner);
        Assert.Empty(links.Items);
    }

    [Fact]
    public async Task Open_FeatureDisabled_ReturnsTheGenericAnswerAndKeepsTheLink()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        using var host = DisabledHost();
        using var unknown = await OpenOnAsync(host, Guid.NewGuid());
        using var disabled = await OpenOnAsync(host, linkId);

        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        Assert.Equal(unknown.StatusCode, disabled.StatusCode);
        Assert.Equal(
            NormalizedProblem(await unknown.Content.ReadAsStringAsync(Token)),
            NormalizedProblem(await disabled.Content.ReadAsStringAsync(Token)));
        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task FeatureDisabled_ListRevokeAndNoticesStillWork()
    {
        var secret = await NewSecretAsync();
        var openedLink = await CreateLinkAsync(secret.Id, NewPayload(out _));
        var listedLink = await CreateLinkAsync(secret.Id, NewPayload(out _));
        using (var opened = await OpenAsync(openedLink))
        {
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        }

        using var host = DisabledHost();
        using var client = CreateClientFor(host, TestTokens.Create(Owner));

        using var listResponse = await client.GetAsync(ApiRoutes.OneTimeLinks, Token);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<OneTimeLinkPageDto>(Token);
        Assert.Contains(page!.Items, link => link.Id == listedLink);

        using var noticeResponse = await client.GetAsync(ApiRoutes.OneTimeLinkNotices, Token);
        Assert.Equal(HttpStatusCode.OK, noticeResponse.StatusCode);
        var notices = await noticeResponse.Content.ReadFromJsonAsync<OneTimeLinkNoticeListDto>(Token);
        var notice = Assert.Single(notices!.Items, item => item.LinkId == openedLink);

        using var revoke = await client.DeleteAsync(LinkRoute(listedLink), Token);
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        using var dismiss = await client.DeleteAsync(ApiRoutes.OneTimeLinkNotice.Replace("{noticeId}", Escape(notice.NoticeId), StringComparison.Ordinal), Token);
        Assert.Equal(HttpStatusCode.NoContent, dismiss.StatusCode);
    }

    private WebApplicationFactory<Program> DisabledHost() =>
        Factory.WithWebHostBuilder(builder => builder.UseSetting("OneTimeLinks:Enabled", "false"));

    private static async Task<HttpResponseMessage> OpenOnAsync(WebApplicationFactory<Program> host, Guid linkId)
    {
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent);
        using var client = CreateClientFor(host);
        return await client.SendAsync(request, Token);
    }

    private async Task<OneTimeLinkPageDto> ListAsync(TestUser user)
    {
        using var client = ClientFor(user);
        using var response = await client.GetAsync(ApiRoutes.OneTimeLinks, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OneTimeLinkPageDto>(Token))!;
    }
}
