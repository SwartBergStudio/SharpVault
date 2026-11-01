// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

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
    public async Task AccountPage_SignedInUser_RendersProfileFromApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Matches($"id=\"account-display-name\"[^>]*>{TestUsers.User.DisplayName}<", html);
        Assert.Matches($"id=\"account-user-id\"[^>]*>{TestUsers.User.ObjectId:D}<", html);
        var users = _api.StorageAdminClient.GetTableClient(TableNames.Users);
        Assert.Equal(1, await UserRows.CountAsync(users, cancellationToken));
        Assert.NotNull(await UserRows.GetAsync(users, TestUsers.User.ObjectId, cancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_SignedInAdmin_RendersAdminBadge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.Admin, cancellationToken);

        var html = await client.GetStringAsync("/account", cancellationToken);

        Assert.Contains("id=\"account-admin-badge\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_SignedInUser_HasNoAdminBadge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync("/account", cancellationToken);

        Assert.DoesNotContain("id=\"account-admin-badge\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_SignedInUser_ShowsOnlyTheirOwnIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _api.SeedUserAsync(TestUsers.OtherUser, UserRows.ActiveStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var client = await _web.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync("/account", cancellationToken);

        Assert.DoesNotContain(TestUsers.OtherUser.ObjectId.ToString("D"), html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestUsers.OtherUser.DisplayName, html, StringComparison.Ordinal);
        Assert.DoesNotContain(TestUsers.OtherUser.Email!, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_LockedUser_RedirectsToAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _api.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var client = await _web.CreateSignedInClientAsync(TestUsers.LockedUser, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/access-denied", new Uri(new Uri("https://localhost"), response.Headers.Location!).AbsolutePath);
        Assert.DoesNotContain(TestUsers.LockedUser.DisplayName, await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_LockedAdmin_RedirectsToAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _api.SeedUserAsync(TestUsers.LockedAdmin, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var client = await _web.CreateSignedInClientAsync(TestUsers.LockedAdmin, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/access-denied", new Uri(new Uri("https://localhost"), response.Headers.Location!).AbsolutePath);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_NoRoleUser_RedirectsToAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.AuthenticatedWithoutRole, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/access-denied", new Uri(new Uri("https://localhost"), response.Headers.Location!).AbsolutePath);
        Assert.Equal(0, await UserRows.CountAsync(_api.StorageAdminClient.GetTableClient(TableNames.Users), cancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_GuestUser_RedirectsToAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.Guest, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/access-denied", new Uri(new Uri("https://localhost"), response.Headers.Location!).AbsolutePath);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_OtherTenantUser_DoesNotRenderProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.OtherTenantUser, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, await UserRows.CountAsync(_api.StorageAdminClient.GetTableClient(TableNames.Users), cancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_Rendered_ContainsNoAccessToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);
        Assert.DoesNotContain("eyJ", html, StringComparison.Ordinal);
        foreach (var segment in token.Split('.'))
        {
            Assert.DoesNotContain(segment, html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.Contains("eyJ", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_ApiCall_TokenNotLoggedByWebOrApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);
        var parts = token.Split('.');
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
    public async Task AccountPage_SignedIn_NamesAndEmailsAreNotLoggedByWebOrApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        await client.GetAsync("/account", cancellationToken);

        foreach (var logs in new[] { _web.Logs, _api.Logs })
        {
            Assert.False(logs.ContainsText(TestUsers.User.Email!), "The email address was logged.");
            Assert.False(logs.ContainsText(TestUsers.User.DisplayName), "The display name was logged.");
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AccountPage_SignedIn_NeverQueriesStorageWithoutPartitionKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await _web.CreateSignedInClientAsync(TestUsers.User, cancellationToken);

        await client.GetAsync("/account", cancellationToken);

        Assert.Empty(_api.StorageRequests.QueriesWithoutPartitionKey());
    }
}
