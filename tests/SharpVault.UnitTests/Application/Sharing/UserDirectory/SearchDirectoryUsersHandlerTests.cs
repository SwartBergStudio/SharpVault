// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Directory;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Directory;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.UserDirectory;

public sealed class SearchDirectoryUsersHandlerTests
{
    private readonly IUserDirectory _directory = Substitute.For<IUserDirectory>();
    private readonly IUserStore _users = Substitute.For<IUserStore>();
    private readonly StubCurrentUser _caller = new();

    private SearchDirectoryUsersQueryHandler CreateHandler(IUserDirectory? directory = null) =>
        new(directory ?? _directory, _users, _caller);

    private static DirectoryUser Candidate(string name) =>
        new(Guid.NewGuid(), name, name.Replace(' ', '.') + "@example.test");

    private void StoreStatus(Guid id, UserStatus status) =>
        _users.GetAsync(id, Arg.Any<CancellationToken>()).Returns(
            new Versioned<User>(new User { Id = id, DisplayName = "row", Status = status }, "etag"));

    private void Found(params DirectoryUser[] found) =>
        _directory.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(found);

    [Fact]
    public async Task Handle_ActiveMatch_ReturnsIdNameAndEmail()
    {
        var anna = Candidate("Anna Smith");
        Found(anna);
        StoreStatus(anna.Id, UserStatus.Active);

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("anna"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(anna.Id, item.Id);
        Assert.Equal("Anna Smith", item.DisplayName);
        Assert.Equal(anna.Email, item.Email);
    }

    [Fact]
    public async Task Handle_NoMatch_ReturnsEmptyListAndReadsNoStatus()
    {
        Found();

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("nobody"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        await _users.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TermWithPadding_IsTrimmedAndAskedForOneMoreThanTheLimit()
    {
        Found();

        await CreateHandler().Handle(new SearchDirectoryUsersQuery("  anna  "), CancellationToken.None);

        await _directory.Received(1).SearchAsync("anna", DirectorySearchLimits.ResultLimit + 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CancellationToken_ReachesTheDirectoryAndTheStore()
    {
        using var source = new CancellationTokenSource();
        var anna = Candidate("Anna Smith");
        Found(anna);
        StoreStatus(anna.Id, UserStatus.Active);

        await CreateHandler().Handle(new SearchDirectoryUsersQuery("anna"), source.Token);

        await _directory.Received(1).SearchAsync(Arg.Any<string>(), Arg.Any<int>(), source.Token);
        await _users.Received(1).GetAsync(anna.Id, Arg.Is<CancellationToken>(token => token.CanBeCanceled));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_CallerAmongMatches_IsRemovedAndNeverLookedUp()
    {
        var self = new DirectoryUser(_caller.UserId, "Me Myself", "me@example.test");
        var other = Candidate("Other Person");
        Found(self, other);
        StoreStatus(other.Id, UserStatus.Active);
        StoreStatus(self.Id, UserStatus.Active);

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("person"), CancellationToken.None);

        Assert.Equal([other.Id], result.Value!.Items.Select(item => item.Id));
        await _users.DidNotReceive().GetAsync(_caller.UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OnlyTheCallerMatches_ReturnsEmptyList()
    {
        Found(new DirectoryUser(_caller.UserId, "Me Myself", null));

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("myself"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_LockedUser_IsRemoved()
    {
        var locked = Candidate("Locked Larry");
        var active = Candidate("Active Anna");
        Found(locked, active);
        StoreStatus(locked.Id, UserStatus.Locked);
        StoreStatus(active.Id, UserStatus.Active);

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("larry"), CancellationToken.None);

        Assert.Equal([active.Id], result.Value!.Items.Select(item => item.Id));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_UserWithNoRow_IsKeptBecauseTheDirectoryConfirmedMembership()
    {
        var newcomer = Candidate("New Comer");
        Found(newcomer);
        _users.GetAsync(newcomer.Id, Arg.Any<CancellationToken>()).Returns((Versioned<User>?)null);

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("comer"), CancellationToken.None);

        Assert.Equal([newcomer.Id], result.Value!.Items.Select(item => item.Id));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OnlyMatchIsLocked_IsIndistinguishableFromNoMatch()
    {
        var locked = Candidate("Locked Larry");
        Found(locked);
        StoreStatus(locked.Id, UserStatus.Locked);
        var lockedResult = await CreateHandler().Handle(new SearchDirectoryUsersQuery("larry"), CancellationToken.None);

        var emptyDirectory = Substitute.For<IUserDirectory>();
        emptyDirectory.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DirectoryUser>());
        var unknownResult = await CreateHandler(emptyDirectory).Handle(new SearchDirectoryUsersQuery("larry"), CancellationToken.None);

        Assert.True(lockedResult.IsSuccess);
        Assert.True(unknownResult.IsSuccess);
        Assert.Equal(JsonSerializer.Serialize(unknownResult.Value), JsonSerializer.Serialize(lockedResult.Value));
        Assert.Equal(unknownResult.Errors, lockedResult.Errors);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_Result_ExposesNoStatusRoleOrSignInInformation()
    {
        var anna = Candidate("Anna Smith");
        Found(anna);
        StoreStatus(anna.Id, UserStatus.Active);

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("anna"), CancellationToken.None);

        var json = JsonSerializer.Serialize(result.Value);
        using var document = JsonDocument.Parse(json);
        var properties = document.RootElement.GetProperty("Items")[0].EnumerateObject().Select(p => p.Name).Order();
        Assert.Equal(["DisplayName", "Email", "Id"], properties);
        Assert.Single(document.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Handle_MoreThanTheLimitActive_ReturnsExactlyTheLimitInDirectoryOrder()
    {
        var candidates = Enumerable.Range(0, 11).Select(index => Candidate($"User {index:D2}")).ToArray();
        Found(candidates);
        foreach (var candidate in candidates)
        {
            StoreStatus(candidate.Id, UserStatus.Active);
        }

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("user"), CancellationToken.None);

        Assert.Equal(DirectorySearchLimits.ResultLimit, result.Value!.Items.Count);
        Assert.Equal(candidates.Take(10).Select(c => c.Id), result.Value.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Handle_OneLockedAmongEleven_StillFillsTheLimit()
    {
        var candidates = Enumerable.Range(0, 11).Select(index => Candidate($"User {index:D2}")).ToArray();
        Found(candidates);
        foreach (var candidate in candidates)
        {
            StoreStatus(candidate.Id, candidate == candidates[2] ? UserStatus.Locked : UserStatus.Active);
        }

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("user"), CancellationToken.None);

        Assert.Equal(10, result.Value!.Items.Count);
        Assert.DoesNotContain(candidates[2].Id, result.Value.Items.Select(item => item.Id));
        Assert.Equal(candidates[10].Id, result.Value.Items[^1].Id);
    }

    [Fact]
    public async Task Handle_CallerAndLockedAmongEleven_ReturnsTheRemainder()
    {
        var candidates = Enumerable.Range(0, 11).Select(index => Candidate($"User {index:D2}")).ToArray();
        candidates[0] = new DirectoryUser(_caller.UserId, "Caller", null);
        Found(candidates);
        foreach (var candidate in candidates)
        {
            StoreStatus(candidate.Id, candidate == candidates[5] ? UserStatus.Locked : UserStatus.Active);
        }

        var result = await CreateHandler().Handle(new SearchDirectoryUsersQuery("user"), CancellationToken.None);

        Assert.Equal(9, result.Value!.Items.Count);
    }

    [Fact]
    public async Task Handle_StatusLookups_NeverExceedTheParallelismLimit()
    {
        var candidates = Enumerable.Range(0, 11).Select(index => Candidate($"User {index:D2}")).ToArray();
        Found(candidates);
        var running = 0;
        var peak = 0;
        _users.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var now = Interlocked.Increment(ref running);
            int seen;
            do
            {
                seen = Volatile.Read(ref peak);
            }
            while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) != seen);
            await Task.Delay(20);
            Interlocked.Decrement(ref running);
            var id = call.Arg<Guid>();
            return (Versioned<User>?)new Versioned<User>(new User { Id = id, Status = UserStatus.Active }, "etag");
        });

        await CreateHandler().Handle(new SearchDirectoryUsersQuery("user"), CancellationToken.None);

        Assert.InRange(peak, 1, DirectorySearchLimits.StatusParallelism);
    }

    [Fact]
    public async Task Handle_DirectoryFails_Propagates()
    {
        _directory.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<DirectoryUser>>(_ => throw new HttpRequestException("graph down"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateHandler().Handle(new SearchDirectoryUsersQuery("anna"), CancellationToken.None));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GraphReturnsElevenWithGuestsDropped_ReturnsTheShortPageWithoutError()
    {
        var members = Enumerable.Range(0, 6).Select(index => (Id: Guid.NewGuid(), DisplayName: (string?)$"Member {index}", Mail: (string?)$"m{index}@example.test", UserPrincipalName: (string?)$"m{index}@tenant.test")).ToArray();
        var guests = Enumerable.Range(0, 5).Select(index => (Id: Guid.NewGuid(), DisplayName: (string?)$"Guest {index}", Mail: (string?)$"g{index}@example.test", UserPrincipalName: (string?)$"g{index}_ext.test#EXT#@tenant.test")).ToArray();
        var handler = new FakeGraphHandler();
        handler.Respond(HttpStatusCode.OK, GraphJson.Users([.. guests.Take(3), .. members, .. guests.Skip(3)]));
        var graph = CreateGraph(handler, guestsAllowed: false);
        foreach (var member in members)
        {
            StoreStatus(member.Id, UserStatus.Active);
        }

        var result = await CreateHandler(graph).Handle(new SearchDirectoryUsersQuery("member"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(members.Select(m => m.Id), result.Value!.Items.Select(item => item.Id));
        Assert.Contains("$top=11", handler.Requests[0].Uri.Query);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GraphReturnsOnlyGuests_ReturnsEmptyListWithoutStatusReads()
    {
        var guests = Enumerable.Range(0, 11).Select(index => (Id: Guid.NewGuid(), DisplayName: (string?)$"Guest {index}", Mail: (string?)null, UserPrincipalName: (string?)$"g{index}_ext.test#EXT#@tenant.test")).ToArray();
        var handler = new FakeGraphHandler();
        handler.Respond(HttpStatusCode.OK, GraphJson.Users(guests));

        var result = await CreateHandler(CreateGraph(handler, guestsAllowed: false))
            .Handle(new SearchDirectoryUsersQuery("guest"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        await _users.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_GraphReturnsLockedMemberAndGuest_IsIndistinguishableFromGraphReturningNothing()
    {
        var locked = Guid.NewGuid();
        StoreStatus(locked, UserStatus.Locked);
        var populated = new FakeGraphHandler();
        populated.Respond(HttpStatusCode.OK, GraphJson.Users(
            (locked, "Larry Locked", "larry@example.test", "larry@tenant.test"),
            (Guid.NewGuid(), "Gus Guest", "gus@example.test", "gus_ext.test#EXT#@tenant.test")));
        var nothing = new FakeGraphHandler();

        var first = await CreateHandler(CreateGraph(populated, false)).Handle(new SearchDirectoryUsersQuery("larry"), CancellationToken.None);
        var second = await CreateHandler(CreateGraph(nothing, false)).Handle(new SearchDirectoryUsersQuery("larry"), CancellationToken.None);

        Assert.Equal(JsonSerializer.Serialize(second.Value), JsonSerializer.Serialize(first.Value));
        Assert.Equal(second.IsSuccess, first.IsSuccess);
    }

    private static GraphUserDirectory CreateGraph(FakeGraphHandler handler, bool guestsAllowed) =>
        new(
            new HttpClient(handler),
            new GraphCredential(new RecordingTokenCredential()),
            new StubGuestAccessPolicy { GuestsAllowed = guestsAllowed });
}
