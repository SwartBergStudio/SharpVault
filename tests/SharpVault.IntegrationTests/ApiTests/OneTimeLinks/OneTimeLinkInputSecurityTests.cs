// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.IntegrationTests.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkInputSecurityTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private static readonly TimeSpan ConfiguredMaximum = TimeSpan.FromHours(2);

    [Fact]
    public async Task Create_NullPayload_Returns400()
    {
        var secret = await NewSecretAsync();
        using var content = new StringContent("{\"payload\":null}", System.Text.Encoding.UTF8, "application/json");

        using var response = await ClientFor(Owner).PostAsync(SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, secret.Id), content, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NoLifetime_UsesTwentyFourHoursClampedToTheMaximum()
    {
        var secret = await NewSecretAsync();
        using var host = CappedLifetimeHost();
        using var client = CreateClientFor(host, TestTokens.Create(Owner));

        using var response = await client.PostAsJsonAsync(
            SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, secret.Id),
            new CreateOneTimeLinkRequestDto { Payload = NewPayload(out _) },
            Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OneTimeLinkDto>(Token);
        Assert.NotNull(created);
        Assert.Equal(ConfiguredMaximum, created.ExpiresAt - created.CreatedAt);
    }

    [Fact]
    public async Task Create_LifetimeOverTheConfiguredMaximum_Returns400()
    {
        var secret = await NewSecretAsync();
        using var host = CappedLifetimeHost();
        using var client = CreateClientFor(host, TestTokens.Create(Owner));

        using var response = await client.PostAsJsonAsync(
            SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, secret.Id),
            new CreateOneTimeLinkRequestDto { Payload = NewPayload(out _), LifetimeMinutes = (int)ConfiguredMaximum.TotalMinutes + 1 },
            Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public static TheoryData<string> MalformedTokens =>
    [
        "/",
        "\\",
        "#",
        "?",
        "\u0001",
        "' or PartitionKey ne '",
        "n_0000000000000000001_0123456789abcdef0123456789abcdef\n",
        "0123456789abcdef0123456789abcdef\n",
        "0123456789ABCDEF0123456789ABCDEF",
    ];

    [Theory]
    [MemberData(nameof(MalformedTokens))]
    public async Task DismissNotice_MalformedId_Returns400(string malformed)
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));
        using (var opened = await OpenAsync(linkId))
        {
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        }

        using var response = await ClientFor(Owner).DeleteAsync(NoticeRouteFor(malformed), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single((await OwnerNoticesAsync()).Items);
    }

    [Theory]
    [MemberData(nameof(MalformedTokens))]
    public async Task List_MalformedContinuationToken_Returns400(string malformed)
    {
        using var response = await ClientFor(Owner).GetAsync($"{ApiRoutes.OneTimeLinks}?continuationToken={Uri.EscapeDataString(malformed)}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_TokenFromAnotherUsersLink_NeverReturnsTheirRows()
    {
        var otherSecret = await CreateSecretAsync(ApiFor(TestUsers.OtherUser), new PlaintextMarkers());
        var theirLink = await CreateLinkAsAsync(TestUsers.OtherUser, otherSecret.Id);
        var mine = await NewSecretAsync();
        var myLink = await CreateLinkAsync(mine.Id, NewPayload(out _));

        using var response = await ClientFor(Owner).GetAsync($"{ApiRoutes.OneTimeLinks}?continuationToken={theirLink:N}", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<OneTimeLinkPageDto>(Token);
        Assert.NotNull(page);
        Assert.DoesNotContain(page.Items, link => link.Id == theirLink);
        Assert.All(page.Items, link => Assert.Equal(OwnerId, link.OwnerId));
    }

    private static string NoticeRouteFor(string noticeId) =>
        ApiRoutes.OneTimeLinkNotice.Replace("{noticeId}", Uri.EscapeDataString(noticeId), StringComparison.Ordinal);

    private async Task<Guid> CreateLinkAsAsync(TestUser user, Guid secretId)
    {
        using var response = await PostCreateAsync(user, user.ObjectId, secretId, NewPayload(out _));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkDto>(Token);
        Assert.NotNull(body);
        return body.Id;
    }

    private WebApplicationFactory<Program> CappedLifetimeHost() =>
        Factory.WithWebHostBuilder(builder => builder.UseSetting("OneTimeLinks:MaxLifetime", "02:00:00"));
}
