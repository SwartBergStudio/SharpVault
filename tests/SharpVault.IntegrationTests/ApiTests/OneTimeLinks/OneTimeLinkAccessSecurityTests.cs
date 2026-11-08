// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkAccessSecurityTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    [Theory]
    [InlineData(SharePermissionDto.View)]
    [InlineData(SharePermissionDto.Edit)]
    public async Task Snapshot_SharedWithViewOrEdit_Returns404(SharePermissionDto permission)
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.OtherUser, permission);

        using var response = await PostSnapshotAsync(TestUsers.OtherUser, OwnerId, secret.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(SharePermissionDto.View)]
    [InlineData(SharePermissionDto.Edit)]
    public async Task Create_SharedWithViewOrEdit_Returns404AndStoresNothing(SharePermissionDto permission)
    {
        var secret = await NewSecretAsync();
        await ShareSecretAsync(secret.Id, TestUsers.OtherUser, permission);

        using var response = await PostCreateAsync(TestUsers.OtherUser, OwnerId, secret.Id, NewPayload(out _));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var notices = await OwnerNoticesAsync();
        Assert.Empty(notices.Items);
    }

    [Fact]
    public async Task Snapshot_AnotherOwnersIdWithMyOwnSecretId_Returns404()
    {
        var mine = await CreateSecretAsync(ApiFor(TestUsers.OtherUser), new PlaintextMarkers());

        using var response = await PostSnapshotAsync(TestUsers.OtherUser, OwnerId, mine.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(nameof(TestUsers.OtherUser))]
    [InlineData(nameof(TestUsers.Admin))]
    public async Task Revoke_AnotherUsersLink_Returns404AndTheLinkStillOpens(string callerName)
    {
        var caller = callerName == nameof(TestUsers.Admin) ? TestUsers.Admin : TestUsers.OtherUser;
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));

        using (var response = await RevokeAsync(caller, linkId))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var opened = await OpenAsync(linkId);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
    }

    [Fact]
    public async Task Revoke_Twice_SecondReturns404AndOneAuditRow()
    {
        var secret = await NewSecretAsync();
        var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));

        using (var first = await RevokeAsync(Owner, linkId))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        using (var second = await RevokeAsync(Owner, linkId))
        {
            Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
        }

        var rows = await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkRevoked, linkId);
        Assert.Single(rows);
    }

    [Fact]
    public async Task OneTimeLinkAuthorizationMatrix_NonUserCallers_ReturnTheExpectedStatusOnEveryCreatorRoute()
    {
        var secret = await NewSecretAsync();
        await WarmUpAsync(TestUsers.LockedUser);
        await LockUserAsync(TestUsers.LockedUser.ObjectId);

        var callers = new (string Name, TestUser? User, HttpStatusCode Expected)[]
        {
            ("unauthenticated", null, HttpStatusCode.Unauthorized),
            ("without role", TestUsers.AuthenticatedWithoutRole, HttpStatusCode.Forbidden),
            ("guest", TestUsers.Guest, HttpStatusCode.Forbidden),
            ("locked user", TestUsers.LockedUser, HttpStatusCode.Forbidden),
        };
        var routes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, ApiRoutes.OneTimeLinkSettings),
            (HttpMethod.Post, SecretRoute(ApiRoutes.OneTimeLinkSecretSnapshot, OwnerId, secret.Id)),
            (HttpMethod.Post, SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, secret.Id)),
            (HttpMethod.Get, ApiRoutes.OneTimeLinks),
            (HttpMethod.Delete, LinkRoute(Guid.NewGuid())),
            (HttpMethod.Get, ApiRoutes.OneTimeLinkNotices),
            (HttpMethod.Delete, ApiRoutes.OneTimeLinkNotice.Replace("{noticeId}", "notice", StringComparison.Ordinal)),
        };

        var mismatches = new List<string>();
        foreach (var (name, user, expected) in callers)
        {
            var client = user is null ? Factory.CreateUnauthenticatedClient() : ClientFor(user);
            foreach (var (method, path) in routes)
            {
                using var request = new HttpRequestMessage(method, path);
                using var response = await client.SendAsync(request, Token);
                if (response.StatusCode != expected)
                {
                    mismatches.Add($"{name} {method} {path}: {(int)response.StatusCode}");
                }
            }
        }

        Assert.Empty(mismatches);
    }
}
