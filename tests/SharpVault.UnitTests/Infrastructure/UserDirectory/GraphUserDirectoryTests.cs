// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using SharpVault.Infrastructure.Directory;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Sharing;

namespace SharpVault.UnitTests.Infrastructure.UserDirectory;

public sealed class GraphUserDirectoryTests : IDisposable
{
    private const string GuestUpn = "outsider_example.test#EXT#@tenant.onmicrosoft.com";
    private const string MemberUpn = "member@tenant.onmicrosoft.com";

    private readonly FakeGraphHandler _handler = new();
    private readonly RecordingTokenCredential _credential = new();
    private readonly StubGuestAccessPolicy _guests = new();

    private GraphUserDirectory CreateDirectory(HttpClient? client = null) =>
        new(client ?? new HttpClient(_handler), new GraphCredential(_credential), _guests);

    public void Dispose() => _handler.Dispose();

    private static Dictionary<string, string> QueryOf(Uri uri) =>
        uri.Query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(parts => Uri.UnescapeDataString(parts[0]), parts => Uri.UnescapeDataString(parts[1]));

    private static string[] RawQueryKeys(Uri uri) =>
        uri.Query.TrimStart('?').Split('&').Select(pair => pair.Split('=', 2)[0]).ToArray();

    [Fact]
    public async Task SearchAsync_SendsConsistencyHeaderAndEscapedTerm()
    {
        await CreateDirectory().SearchAsync("anna smith", 11, CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("eventual", request.ConsistencyLevel);
        Assert.Equal("graph.microsoft.com", request.Uri.Host);
        Assert.Equal("/v1.0/users", request.Uri.AbsolutePath);
        Assert.Contains("anna%20smith", request.Uri.Query);
        Assert.DoesNotContain(' ', request.Uri.Query);
        var query = QueryOf(request.Uri);
        Assert.Equal("\"displayName:anna smith\" OR \"mail:anna smith\"", query["$search"]);
        Assert.Equal("id,displayName,mail,userPrincipalName", query["$select"]);
        Assert.Equal("11", query["$top"]);
    }

    [Fact]
    public async Task SearchAsync_SendsTheBearerTokenFromTheCredential()
    {
        await CreateDirectory().SearchAsync("anna", 11, CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal("graph-token-value", request.AuthorizationParameter);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_RequestsGraphScopeOnly()
    {
        var directory = CreateDirectory();

        await directory.SearchAsync("anna", 11, CancellationToken.None);
        await directory.FindMemberAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, _credential.Scopes.Count);
        Assert.All(_credential.Scopes, scopes => Assert.Equal(["https://graph.microsoft.com/.default"], scopes));
    }

    [Fact]
    public async Task SearchAsync_CancellationToken_ReachesCredentialAndHandler()
    {
        using var source = new CancellationTokenSource();

        await CreateDirectory().SearchAsync("anna", 11, source.Token);

        Assert.Equal(source.Token, Assert.Single(_credential.CancellationTokens));
        Assert.Equal(source.Token, Assert.Single(_guests.Tokens));
    }

    [Fact]
    public async Task SearchAsync_AlreadyCancelled_Throws()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateDirectory().SearchAsync("anna", 11, source.Token));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("a\"b")]
    [InlineData("\"")]
    [InlineData("\"\"\"")]
    [InlineData("\\")]
    [InlineData("a\\b")]
    [InlineData("a\\\"b")]
    [InlineData("\\\"")]
    [InlineData("\" OR \"mail:x")]
    [InlineData("x\" OR displayName:\"y")]
    [InlineData("%22")]
    [InlineData("a%22b")]
    [InlineData("%5C%22")]
    [InlineData("%2522")]
    [InlineData("\\u0022")]
    [InlineData("&$top=999")]
    [InlineData("abc&$filter=accountEnabled eq true")]
    [InlineData("abc#fragment")]
    [InlineData("abc?x=y")]
    [InlineData("abc\r\nConsistencyLevel: none")]
    [InlineData("abc\u0000def")]
    [InlineData("../../me")]
    public async Task SearchAsync_TermIsEscapedAndQuotesCannotAppear(string term)
    {
        await CreateDirectory().SearchAsync(term, 11, CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("graph.microsoft.com", request.Uri.Host);
        Assert.Equal("/v1.0/users", request.Uri.AbsolutePath);
        Assert.Equal(string.Empty, request.Uri.Fragment);
        Assert.Equal(["$search", "$select", "$top"], RawQueryKeys(request.Uri));
        var query = QueryOf(request.Uri);
        var search = query["$search"];
        Assert.Equal(4, search.Count(c => c == '"'));
        Assert.DoesNotContain('\\', search);
        Assert.StartsWith("\"displayName:", search, StringComparison.Ordinal);
        Assert.EndsWith("\"", search, StringComparison.Ordinal);
        Assert.Matches("^\"displayName:[^\"]*\" OR \"mail:[^\"]*\"$", search);
        Assert.Equal("11", query["$top"]);
        Assert.Equal("eventual", request.ConsistencyLevel);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_PercentEncodedQuote_IsNotDecodedIntoAQuote()
    {
        await CreateDirectory().SearchAsync("a%22b", 11, CancellationToken.None);

        var search = QueryOf(Assert.Single(_handler.Requests).Uri)["$search"];

        Assert.Equal("\"displayName:a%22b\" OR \"mail:a%22b\"", search);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_TermWithQuoteAndBackslash_IsStrippedToItsPlainText()
    {
        await CreateDirectory().SearchAsync("a\"\\b", 11, CancellationToken.None);

        var search = QueryOf(Assert.Single(_handler.Requests).Uri)["$search"];

        Assert.Equal("\"displayName:ab\" OR \"mail:ab\"", search);
    }

    [Fact]
    public async Task SearchAsync_NullTerm_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CreateDirectory().SearchAsync(null!, 11, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_Members_AreMappedWithIdNameAndMail()
    {
        var id = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users((id, "Anna Smith", "anna@example.test", MemberUpn)));

        var found = await CreateDirectory().SearchAsync("anna", 11, CancellationToken.None);

        var user = Assert.Single(found);
        Assert.Equal(id, user.Id);
        Assert.Equal("Anna Smith", user.DisplayName);
        Assert.Equal("anna@example.test", user.Email);
    }

    [Fact]
    public async Task SearchAsync_NoDisplayName_FallsBackToUserPrincipalName()
    {
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users((Guid.NewGuid(), null, null, MemberUpn)));

        var found = await CreateDirectory().SearchAsync("member", 11, CancellationToken.None);

        var user = Assert.Single(found);
        Assert.Equal(MemberUpn, user.DisplayName);
        Assert.Null(user.Email);
    }

    [Fact]
    public async Task SearchAsync_NoDisplayNameAndNoUpn_YieldsEmptyName()
    {
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users((Guid.NewGuid(), null, null, null)));

        var found = await CreateDirectory().SearchAsync("member", 11, CancellationToken.None);

        Assert.Equal(string.Empty, Assert.Single(found).DisplayName);
    }

    [Theory]
    [InlineData("{\"value\":[]}")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task SearchAsync_EmptyOrNullBody_ReturnsEmpty(string body)
    {
        _handler.Respond(HttpStatusCode.OK, body);

        var found = await CreateDirectory().SearchAsync("anna", 11, CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task SearchAsync_MalformedBody_Throws()
    {
        _handler.Respond(HttpStatusCode.OK, "<html>");

        await Assert.ThrowsAsync<JsonException>(() => CreateDirectory().SearchAsync("anna", 11, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_GuestUpn_Dropped()
    {
        var member = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users(
            (Guid.NewGuid(), "Outsider", "o@example.test", GuestUpn),
            (member, "Member", "m@example.test", MemberUpn)));
        _guests.GuestsAllowed = false;

        var found = await CreateDirectory().SearchAsync("member", 11, CancellationToken.None);

        Assert.Equal([member], found.Select(user => user.Id));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("x#EXT#@t.onmicrosoft.com")]
    [InlineData("x#ext#@t.onmicrosoft.com")]
    [InlineData("x#Ext#@t.onmicrosoft.com")]
    public async Task SearchAsync_GuestMarkerInAnyCase_Dropped(string upn)
    {
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users((Guid.NewGuid(), "Outsider", null, upn)));

        var found = await CreateDirectory().SearchAsync("outsider", 11, CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task SearchAsync_GuestsAllowed_Kept()
    {
        var guest = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users(
            (guest, "Outsider", "o@example.test", GuestUpn),
            (Guid.NewGuid(), "Member", "m@example.test", MemberUpn)));
        _guests.GuestsAllowed = true;

        var found = await CreateDirectory().SearchAsync("o", 11, CancellationToken.None);

        Assert.Equal(2, found.Count);
        Assert.Contains(guest, found.Select(user => user.Id));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_MemberWithoutUpn_IsNotTreatedAsGuest()
    {
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users((Guid.NewGuid(), "Member", null, null)));

        var found = await CreateDirectory().SearchAsync("member", 11, CancellationToken.None);

        Assert.Single(found);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_GuestMarkerOnlyInDisplayNameOrMail_IsNotTreatedAsGuest()
    {
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users((Guid.NewGuid(), "Pat #EXT#", "pat#EXT#@example.test", MemberUpn)));

        var found = await CreateDirectory().SearchAsync("pat", 11, CancellationToken.None);

        Assert.Single(found);
    }

    [Fact]
    public async Task SearchAsync_404_Throws()
    {
        _handler.Respond(HttpStatusCode.NotFound, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateDirectory().SearchAsync("anna", 11, CancellationToken.None));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task SearchAsync_FailureStatus_ThrowsWithoutTermOrTokenInTheMessage(HttpStatusCode status)
    {
        _handler.Respond(status, "{\"error\":\"nope\"}");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateDirectory().SearchAsync("needle-marker", 11, CancellationToken.None));

        Assert.DoesNotContain("needle-marker", failure.Message);
        Assert.DoesNotContain("graph-token-value", failure.Message);
    }

    [Fact]
    public async Task SearchAsync_500_Throws()
    {
        _handler.Respond(HttpStatusCode.InternalServerError, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateDirectory().SearchAsync("anna", 11, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_CredentialFails_PropagatesAndSendsNothing()
    {
        _credential.Failure = new InvalidOperationException("no token");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateDirectory().SearchAsync("anna", 11, CancellationToken.None));

        Assert.Empty(_handler.Requests);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_GuestPolicyFails_PropagatesRatherThanReturningGuests()
    {
        _handler.Respond(HttpStatusCode.OK, GraphJson.Users((Guid.NewGuid(), "Outsider", null, GuestUpn)));
        var failing = new FailingGuestPolicy();
        var directory = new GraphUserDirectory(new HttpClient(_handler), new GraphCredential(_credential), failing);

        await Assert.ThrowsAsync<InvalidOperationException>(() => directory.SearchAsync("outsider", 11, CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task BaseAddress_ConfiguredElsewhere_IsOverriddenWithTheConstantGraphHost()
    {
        var client = new HttpClient(_handler) { BaseAddress = new Uri("https://evil.example/v1.0/") };

        var directory = CreateDirectory(client);
        await directory.SearchAsync("anna", 11, CancellationToken.None);
        await directory.FindMemberAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(new Uri("https://graph.microsoft.com/v1.0/"), client.BaseAddress);
        Assert.All(_handler.Requests, request => Assert.Equal("graph.microsoft.com", request.Uri.Host));
        Assert.Equal("https", _handler.Requests[0].Uri.Scheme);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GraphBaseAddress_IsTheHttpsGraphV1Constant()
    {
        Assert.Equal("https://graph.microsoft.com/v1.0/", GraphUserDirectory.GraphBaseAddress);
        Assert.Equal("https://graph.microsoft.com/.default", GraphUserDirectory.GraphScope);
    }

    [Fact]
    public async Task FindMemberAsync_Found_SendsIdInTheRouteAndMapsTheUser()
    {
        var id = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, GraphJson.User(id, "Anna", "anna@example.test", MemberUpn));

        var found = await CreateDirectory().FindMemberAsync(id, CancellationToken.None);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal($"/v1.0/users/{id:D}", request.Uri.AbsolutePath);
        Assert.Equal("id,displayName,mail,userPrincipalName", QueryOf(request.Uri)["$select"]);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.NotNull(found);
        Assert.Equal(id, found.Id);
        Assert.Equal("Anna", found.DisplayName);
    }

    [Fact]
    public async Task FindMemberAsync_404_ReturnsNull()
    {
        _handler.Respond(HttpStatusCode.NotFound, "{}");

        var found = await CreateDirectory().FindMemberAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(found);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task FindMemberAsync_OtherFailure_Throws(HttpStatusCode status)
    {
        _handler.Respond(status, "{}");

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateDirectory().FindMemberAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task FindMemberAsync_Guest_ReturnsNullWhenGuestsNotAllowed()
    {
        var id = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, GraphJson.User(id, "Outsider", null, GuestUpn));
        _guests.GuestsAllowed = false;

        var found = await CreateDirectory().FindMemberAsync(id, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task FindMemberAsync_Guest_ReturnedWhenGuestsAllowed()
    {
        var id = Guid.NewGuid();
        _handler.Respond(HttpStatusCode.OK, GraphJson.User(id, "Outsider", null, GuestUpn));
        _guests.GuestsAllowed = true;

        var found = await CreateDirectory().FindMemberAsync(id, CancellationToken.None);

        Assert.NotNull(found);
    }

    [Fact]
    public async Task FindMemberAsync_NullBody_ReturnsNull()
    {
        _handler.Respond(HttpStatusCode.OK, "null");

        var found = await CreateDirectory().FindMemberAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphUserDirectory(null!, new GraphCredential(_credential), _guests));
        Assert.Throws<ArgumentNullException>(() => new GraphUserDirectory(new HttpClient(_handler), null!, _guests));
        Assert.Throws<ArgumentNullException>(() => new GraphUserDirectory(new HttpClient(_handler), new GraphCredential(_credential), null!));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GraphCredential_ToString_ReturnsTypeNameOnly()
    {
        Assert.Equal(nameof(GraphCredential), new GraphCredential(_credential).ToString());
    }

    private sealed class FailingGuestPolicy : SharpVault.Application.Common.Interfaces.IGuestAccessPolicy
    {
        public ValueTask<bool> AreGuestsAllowedAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("policy unavailable");
    }
}
