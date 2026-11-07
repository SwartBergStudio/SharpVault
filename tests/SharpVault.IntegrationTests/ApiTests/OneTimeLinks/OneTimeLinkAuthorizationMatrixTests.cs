// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkAuthorizationMatrixTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private const string Settings = "E1";
    private const string Snapshot = "E2";
    private const string Create = "E3";
    private const string List = "E4";
    private const string Revoke = "E5";
    private const string Notices = "E6";
    private const string Dismiss = "E7";

    private static readonly string[] EndpointIds = [Settings, Snapshot, Create, List, Revoke, Notices, Dismiss];

    private static readonly CallerKind[] Callers =
    [
        CallerKind.Anonymous,
        CallerKind.AlgNoneToken,
        CallerKind.UnknownKeyToken,
        CallerKind.OtherTenantUser,
        CallerKind.Guest,
        CallerKind.LockedUser,
        CallerKind.LockedAdmin,
        CallerKind.AuthenticatedWithoutRole,
        CallerKind.User,
        CallerKind.ShareManager,
        CallerKind.ShareViewer,
        CallerKind.ShareEditor,
        CallerKind.FolderManager,
        CallerKind.OtherUser,
        CallerKind.Admin,
    ];

    public static TheoryData<string, CallerKind, HttpStatusCode> MatrixCases()
    {
        var cases = new TheoryData<string, CallerKind, HttpStatusCode>();
        foreach (var caller in Callers)
        {
            foreach (var endpoint in EndpointIds)
            {
                cases.Add(endpoint, caller, ExpectedStatus(endpoint, caller));
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task OneTimeLinkAuthorizationMatrix_EveryEndpointAndCaller_ReturnsExpectedStatus(string endpoint, CallerKind caller, HttpStatusCode expected)
    {
        var scenario = await ArrangeAsync();
        using var client = CallerClients.CreateFor(Factory, caller);

        using var response = await SendAsync(client, endpoint, caller, scenario);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task OneTimeLinkAuthorizationMatrix_EveryEndpoint_NoQueryWithoutPartitionKey()
    {
        var scenario = await ArrangeAsync();
        var callers = new[] { CallerKind.User, CallerKind.ShareManager, CallerKind.ShareViewer, CallerKind.OtherUser, CallerKind.Admin };
        var clients = callers.Select(caller => (caller, client: CallerClients.CreateFor(Factory, caller))).ToList();
        ResetCounters();

        try
        {
            foreach (var (caller, client) in clients)
            {
                foreach (var endpoint in EndpointIds)
                {
                    using var response = await SendAsync(client, endpoint, caller, scenario);
                }
            }

            AssertNoQueryWithoutPartitionKey();
        }
        finally
        {
            foreach (var (_, client) in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task Revoke_OwnerOnALinkTheirManagerCreated_Returns404()
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        var managerLink = await CreateLinkAsAsync(TestUsers.ShareManager, secret.Id);

        using (var revoke = await RevokeAsync(Owner, managerLink))
        {
            Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
        }

        var ownerList = await ListAsync(Owner);
        Assert.DoesNotContain(ownerList.Items, link => link.Id == managerLink);

        using (var opened = await OpenAsync(managerLink))
        {
            Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        }

        var managerNotice = Assert.Single((await NoticesAsync(TestUsers.ShareManager)).Items, notice => notice.LinkId == managerLink);
        using (var dismiss = await ClientFor(Owner).DeleteAsync(NoticeRoute(managerNotice.NoticeId), Token))
        {
            Assert.Equal(HttpStatusCode.NotFound, dismiss.StatusCode);
        }

        Assert.Single((await NoticesAsync(TestUsers.ShareManager)).Items, notice => notice.NoticeId == managerNotice.NoticeId);
    }

    private static HttpStatusCode ExpectedStatus(string endpoint, CallerKind caller) => caller switch
    {
        CallerKind.Anonymous or CallerKind.AlgNoneToken or CallerKind.UnknownKeyToken or CallerKind.OtherTenantUser => HttpStatusCode.Unauthorized,
        CallerKind.Guest or CallerKind.LockedUser or CallerKind.LockedAdmin or CallerKind.AuthenticatedWithoutRole => HttpStatusCode.Forbidden,
        CallerKind.User or CallerKind.ShareManager or CallerKind.FolderManager => ExpectedForManager(endpoint),
        _ => ExpectedForReader(endpoint),
    };

    private static HttpStatusCode ExpectedForManager(string endpoint) => endpoint switch
    {
        Settings or Snapshot or List or Notices => HttpStatusCode.OK,
        Create => HttpStatusCode.Created,
        _ => HttpStatusCode.NoContent,
    };

    private static HttpStatusCode ExpectedForReader(string endpoint) => endpoint switch
    {
        Settings or List or Notices => HttpStatusCode.OK,
        _ => HttpStatusCode.NotFound,
    };

    private async Task<Scenario> ArrangeAsync()
    {
        var folder = await NewFolderAsync("Matrix folder");
        var secret = await NewSecretAsync(folderId: folder.Id);
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        await ShareFolderAsync(folder.Id, TestUsers.FolderManager, SharePermissionDto.Manage);
        await WarmUpAsync(TestUsers.LockedUser);
        await LockUserAsync(TestUsers.LockedUser.ObjectId);
        await WarmUpAsync(TestUsers.LockedAdmin);
        await LockUserAsync(TestUsers.LockedAdmin.ObjectId);

        var ownerLink = await CreateLinkAsAsync(Owner, secret.Id);
        var managerLink = await CreateLinkAsAsync(TestUsers.ShareManager, secret.Id);
        var ownerNoticeLink = await CreateLinkAsAsync(Owner, secret.Id);
        var managerNoticeLink = await CreateLinkAsAsync(TestUsers.ShareManager, secret.Id);
        var folderManagerLink = await CreateLinkAsAsync(TestUsers.FolderManager, secret.Id);
        var folderManagerNoticeLink = await CreateLinkAsAsync(TestUsers.FolderManager, secret.Id);
        using (var openedOwner = await OpenAsync(ownerNoticeLink))
        {
            Assert.Equal(HttpStatusCode.OK, openedOwner.StatusCode);
        }

        using (var openedManager = await OpenAsync(managerNoticeLink))
        {
            Assert.Equal(HttpStatusCode.OK, openedManager.StatusCode);
        }

        using (var openedFolderManager = await OpenAsync(folderManagerNoticeLink))
        {
            Assert.Equal(HttpStatusCode.OK, openedFolderManager.StatusCode);
        }

        var ownerNotice = Assert.Single((await NoticesAsync(Owner)).Items, notice => notice.LinkId == ownerNoticeLink);
        var managerNotice = Assert.Single((await NoticesAsync(TestUsers.ShareManager)).Items, notice => notice.LinkId == managerNoticeLink);
        var folderManagerNotice = Assert.Single((await NoticesAsync(TestUsers.FolderManager)).Items, notice => notice.LinkId == folderManagerNoticeLink);
        return new Scenario(
            secret.Id,
            ownerLink,
            managerLink,
            folderManagerLink,
            ownerNotice.NoticeId,
            managerNotice.NoticeId,
            folderManagerNotice.NoticeId);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string endpoint, CallerKind caller, Scenario scenario)
    {
        var isManager = caller == CallerKind.ShareManager;
        var isFolderManager = caller == CallerKind.FolderManager;
        return endpoint switch
        {
            Settings => await client.GetAsync(ApiRoutes.OneTimeLinkSettings, Token),
            Snapshot => await client.PostAsync(SecretRoute(ApiRoutes.OneTimeLinkSecretSnapshot, OwnerId, scenario.SecretId), null, Token),
            Create => await client.PostAsJsonAsync(
                SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, scenario.SecretId),
                new CreateOneTimeLinkRequestDto { Payload = NewPayload(out _) },
                Token),
            List => await client.GetAsync(ApiRoutes.OneTimeLinks, Token),
            Revoke => await client.DeleteAsync(LinkRoute(isFolderManager ? scenario.FolderManagerLinkId : isManager ? scenario.ManagerLinkId : scenario.OwnerLinkId), Token),
            Notices => await client.GetAsync(ApiRoutes.OneTimeLinkNotices, Token),
            Dismiss => await client.DeleteAsync(NoticeRoute(isFolderManager ? scenario.FolderManagerNoticeId : isManager ? scenario.ManagerNoticeId : scenario.OwnerNoticeId), Token),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
        };
    }

    private static string NoticeRoute(string noticeId) =>
        ApiRoutes.OneTimeLinkNotice.Replace("{noticeId}", Escape(noticeId), StringComparison.Ordinal);

    private async Task<Guid> CreateLinkAsAsync(TestUser user, Guid secretId)
    {
        using var response = await PostCreateAsync(user, OwnerId, secretId, NewPayload(out _));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkDto>(Token);
        Assert.NotNull(body);
        return body.Id;
    }

    private async Task<OneTimeLinkPageDto> ListAsync(TestUser user)
    {
        using var client = ClientFor(user);
        using var response = await client.GetAsync(ApiRoutes.OneTimeLinks, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkPageDto>(Token);
        Assert.NotNull(body);
        return body;
    }

    private async Task<OneTimeLinkNoticeListDto> NoticesAsync(TestUser user)
    {
        using var client = ClientFor(user);
        using var response = await client.GetAsync(ApiRoutes.OneTimeLinkNotices, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkNoticeListDto>(Token);
        Assert.NotNull(body);
        return body;
    }

    private sealed record Scenario(
        Guid SecretId,
        Guid OwnerLinkId,
        Guid ManagerLinkId,
        Guid FolderManagerLinkId,
        string OwnerNoticeId,
        string ManagerNoticeId,
        string FolderManagerNoticeId);
}
