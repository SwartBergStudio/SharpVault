// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Users;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebApiTestGroup))]
public sealed class AccountPageApiTests : IAsyncLifetime
{
    private readonly ApiTestFactory _api;
    private readonly WebTestFactory _web;

    public AccountPageApiTests(ApiTestFactory api, WebTestFactory web)
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

    [Fact]
    public async Task Me_ApiClientThroughTheRealApi_ReturnsTheSignedInProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);

        var response = await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(TestUsers.User.DisplayName, response.Value!.DisplayName);
        Assert.Equal(TestUsers.User.ObjectId, response.Value.UserId);
        Assert.False(response.Value.IsAdmin);
        var users = _api.StorageAdminClient.GetTableClient(TableNames.Users);
        Assert.Equal(1, await UserRows.CountAsync(users, cancellationToken));
        Assert.NotNull(await UserRows.GetAsync(users, TestUsers.User.ObjectId, cancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Me_SignedInAdmin_ReturnsIsAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.Admin);

        var response = await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.True(response.Value!.IsAdmin);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Me_SignedInUser_ShowsOnlyTheirOwnIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _api.SeedUserAsync(TestUsers.OtherUser, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var circuit = WebCircuit.For(_web, TestUsers.User);

        var response = await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.Equal(TestUsers.User.ObjectId, response.Value!.UserId);
        Assert.NotEqual(TestUsers.OtherUser.ObjectId, response.Value.UserId);
        Assert.NotEqual(TestUsers.OtherUser.DisplayName, response.Value.DisplayName);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("LockedUser")]
    [InlineData("LockedAdmin")]
    public async Task Me_LockedUser_ReturnsAccessDenied(string name)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var locked = name == "LockedAdmin" ? TestUsers.LockedAdmin : TestUsers.LockedUser;
        await _api.SeedUserAsync(locked, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var circuit = WebCircuit.For(_web, locked);

        var response = await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.Equal(ApiCallStatus.AccessDenied, response.Status);
        Assert.Null(response.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Me_NoRoleUser_ReturnsAccessDeniedAndCreatesNoRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.AuthenticatedWithoutRole);

        var response = await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.Equal(ApiCallStatus.AccessDenied, response.Status);
        Assert.Equal(0, await UserRows.CountAsync(_api.StorageAdminClient.GetTableClient(TableNames.Users), cancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Me_GuestUser_ReturnsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.Guest);

        var response = await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.Equal(ApiCallStatus.AccessDenied, response.Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Me_OtherTenantUser_DoesNotReturnTheProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.OtherTenantUser);

        var response = await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.NotEqual(ApiCallStatus.Success, response.Status);
        Assert.Null(response.Value);
        Assert.Equal(0, await UserRows.CountAsync(_api.StorageAdminClient.GetTableClient(TableNames.Users), cancellationToken));
    }

    [Fact]
    public async Task CurrentUserAccessor_TwoReadsInOneCircuit_ReturnTheSameCachedProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);

        var first = await circuit.CurrentUser.GetAsync(cancellationToken);
        var second = await circuit.CurrentUser.GetAsync(cancellationToken);

        Assert.Equal(ApiCallStatus.Success, first.Status);
        Assert.Same(first.Value, second.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AdminUsers_AdminThroughTheRealApi_ListsDirectoryFieldsOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _api.SeedUserAsync(TestUsers.OtherUser, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var circuit = WebCircuit.For(_web, TestUsers.Admin);

        var response = await circuit.AdminUsers.ListUsersAsync(50, null, cancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Contains(response.Value!.Users, user => user.UserId == TestUsers.OtherUser.ObjectId && user.Status == UserStatusDto.Active);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AdminUsers_UserThroughTheRealApi_ReturnsAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);

        var response = await circuit.AdminUsers.ListUsersAsync(50, null, cancellationToken);

        Assert.Equal(ApiCallStatus.AccessDenied, response.Status);
        Assert.Null(response.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Shell_SignedIn_HtmlContainsNoTokenOrSegmentOfIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);
        Assert.False(WebTestSupport.ContainsJwt(html), "The page contains a JWT-shaped string.");
        foreach (var segment in token.Split('.'))
        {
            Assert.DoesNotContain(segment, html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => WebTestSupport.ContainsJwt(cookie));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Me_ApiCall_TokenNotLoggedByWebOrApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);

        var response = await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        var parts = TestTokens.ForObjectId(TestUsers.User.ObjectId).Split('.');
        foreach (var logs in new[] { _web.Logs, _api.Logs })
        {
            Assert.False(logs.ContainsText(parts[0] + "." + parts[1]), "A token header and payload were logged.");
            Assert.False(logs.ContainsText(parts[1]), "A token payload was logged.");
            Assert.False(logs.ContainsText(parts[2]), "A token signature was logged.");
            Assert.False(logs.ContainsText("Bearer eyJ"), "An authorization header value was logged.");
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Me_SignedIn_NamesAndEmailsAreNotLoggedByWebOrApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);

        await circuit.Me.GetCurrentUserAsync(cancellationToken);

        foreach (var logs in new[] { _web.Logs, _api.Logs })
        {
            Assert.False(logs.ContainsText(TestUsers.User.Email!), "The email address was logged.");
            Assert.False(logs.ContainsText(TestUsers.User.DisplayName), "The display name was logged.");
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Me_SignedIn_NeverQueriesStorageWithoutPartitionKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);

        await circuit.Me.GetCurrentUserAsync(cancellationToken);

        Assert.Empty(_api.StorageRequests.QueriesWithoutPartitionKey());
    }
}
