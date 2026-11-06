// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Directory;
using SharpVault.Domain.Users;
using SharpVault.Infrastructure.Directory;

namespace SharpVault.UnitTests.Infrastructure.UserDirectory;

public sealed class TableUserDirectoryTests
{
    private readonly IUserStore _store = Substitute.For<IUserStore>();

    private static User Row(string name, string? email = null, UserStatus status = UserStatus.Active) =>
        new() { Id = Guid.NewGuid(), DisplayName = name, Email = email, Status = status };

    private void Pages(params User[][] pages)
    {
        var tokens = pages.Select(_ => (Guid?)Guid.NewGuid()).ToArray();
        tokens[^1] = null;
        var calls = 0;
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var index = Math.Min(calls++, pages.Length - 1);
            return new UserListPage(pages[index], tokens[index]);
        });
    }

    [Fact]
    public async Task SearchAsync_DisplayNameContainsTerm_ReturnsMatch()
    {
        var anna = Row("Anna Smith", "as@example.test");
        Pages([anna, Row("Bob Jones")]);

        var found = await new TableUserDirectory(_store).SearchAsync("smith", 10, CancellationToken.None);

        var match = Assert.Single(found);
        Assert.Equal(new DirectoryUser(anna.Id, "Anna Smith", "as@example.test"), match);
    }

    [Fact]
    public async Task SearchAsync_EmailContainsTerm_ReturnsMatch()
    {
        var bob = Row("Robert", "bob.jones@example.test");
        Pages([Row("Anna"), bob]);

        var found = await new TableUserDirectory(_store).SearchAsync("jones", 10, CancellationToken.None);

        Assert.Equal([bob.Id], found.Select(user => user.Id));
    }

    [Theory]
    [InlineData("ANNA")]
    [InlineData("anna")]
    [InlineData("AnNa")]
    public async Task SearchAsync_DifferentCase_Matches(string term)
    {
        Pages([Row("Anna Smith")]);

        var found = await new TableUserDirectory(_store).SearchAsync(term, 10, CancellationToken.None);

        Assert.Single(found);
    }

    [Fact]
    public async Task SearchAsync_NullEmail_DoesNotThrowAndMatchesOnName()
    {
        Pages([Row("Anna Smith", null), Row("Other", null)]);

        var found = await new TableUserDirectory(_store).SearchAsync("anna", 10, CancellationToken.None);

        Assert.Single(found);
        Assert.Null(found[0].Email);
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        Pages([Row("Anna"), Row("Bob")]);

        var found = await new TableUserDirectory(_store).SearchAsync("zzz", 10, CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task SearchAsync_MoreMatchesThanLimit_StopsAtLimitAndReadsNoFurtherPage()
    {
        Pages(
            [.. Enumerable.Range(0, 100).Select(index => Row($"match {index}"))],
            [.. Enumerable.Range(0, 100).Select(index => Row($"match later {index}"))]);

        var found = await new TableUserDirectory(_store).SearchAsync("match", 5, CancellationToken.None);

        Assert.Equal(5, found.Count);
        await _store.Received(1).ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_LimitReachedOnPageBoundary_ReturnsExactlyLimit()
    {
        Pages([.. Enumerable.Range(0, 100).Select(index => Row($"match {index}"))]);

        var found = await new TableUserDirectory(_store).SearchAsync("match", 100, CancellationToken.None);

        Assert.Equal(100, found.Count);
    }

    [Fact]
    public async Task SearchAsync_MatchesAcrossPages_FollowsTheContinuationUser()
    {
        var first = Row("alpha one");
        var second = Row("alpha two");
        var tokens = new[] { Guid.NewGuid(), Guid.NewGuid() };
        _store.ListAsync(100, null, Arg.Any<CancellationToken>()).Returns(new UserListPage([first], tokens[0]));
        _store.ListAsync(100, tokens[0], Arg.Any<CancellationToken>()).Returns(new UserListPage([Row("beta")], tokens[1]));
        _store.ListAsync(100, tokens[1], Arg.Any<CancellationToken>()).Returns(new UserListPage([second], null));

        var found = await new TableUserDirectory(_store).SearchAsync("alpha", 10, CancellationToken.None);

        Assert.Equal([first.Id, second.Id], found.Select(user => user.Id));
    }

    [Fact]
    public async Task SearchAsync_NoMatchAndEndlessPages_StopsAfterTheRowCap()
    {
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ =>
            new UserListPage([.. Enumerable.Range(0, 100).Select(index => Row($"nobody {index}"))], Guid.NewGuid()));

        var found = await new TableUserDirectory(_store).SearchAsync("zzz", 10, CancellationToken.None);

        Assert.Empty(found);
        await _store.Received(DirectorySearchLimits.MaxRowsScanned / 100)
            .ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_MatchOnlyBeyondTheRowCap_IsNotFound()
    {
        var calls = 0;
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls++;
            var name = calls == 11 ? "needle" : "hay";
            return new UserListPage([.. Enumerable.Range(0, 100).Select(_ => Row(name))], Guid.NewGuid());
        });

        var found = await new TableUserDirectory(_store).SearchAsync("needle", 10, CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task SearchAsync_EmptyStore_ReturnsEmpty()
    {
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new UserListPage([], null));

        var found = await new TableUserDirectory(_store).SearchAsync("anna", 10, CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task SearchAsync_LockedUserMatching_IsStillReturnedBecauseTheHandlerFilters()
    {
        Pages([Row("Locked Larry", status: UserStatus.Locked)]);

        var found = await new TableUserDirectory(_store).SearchAsync("larry", 10, CancellationToken.None);

        Assert.Single(found);
    }

    [Fact]
    public async Task SearchAsync_NullTerm_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new TableUserDirectory(_store).SearchAsync(null!, 10, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_CancellationToken_ReachesTheStore()
    {
        using var source = new CancellationTokenSource();
        Pages([Row("Anna")]);

        await new TableUserDirectory(_store).SearchAsync("anna", 10, source.Token);

        await _store.Received(1).ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), source.Token);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("' or PartitionKey ne '")]
    [InlineData("x' or RowKey ge '")]
    [InlineData("a%27 or true")]
    [InlineData("*")]
    [InlineData("\"; drop")]
    public async Task SearchAsync_ODataSyntaxInTerm_NeverReachesStorage(string term)
    {
        Pages([Row("Anna"), Row("Bob")]);

        var found = await new TableUserDirectory(_store).SearchAsync(term, 10, CancellationToken.None);

        Assert.Empty(found);
        var calls = _store.ReceivedCalls().ToList();
        Assert.All(calls, call =>
        {
            Assert.Equal(nameof(IUserStore.ListAsync), call.GetMethodInfo().Name);
            Assert.DoesNotContain(call.GetArguments(), argument => argument is string);
            Assert.Equal(100, call.GetArguments()[0]);
        });
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_ODataSyntaxInTerm_MatchesOnlyAsALiteral()
    {
        var literal = Row("it's' or PartitionKey ne 'x");
        Pages([literal, Row("Anna")]);

        var found = await new TableUserDirectory(_store).SearchAsync("' or PartitionKey ne '", 10, CancellationToken.None);

        Assert.Equal([literal.Id], found.Select(user => user.Id));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task SearchAsync_Wildcards_AreLiteralNotPatterns()
    {
        Pages([Row("Anna"), Row("Bob")]);

        var found = await new TableUserDirectory(_store).SearchAsync("*", 10, CancellationToken.None);

        Assert.Empty(found);
    }

    [Fact]
    public async Task FindMemberAsync_Existing_ReturnsDirectoryUser()
    {
        var anna = Row("Anna", "a@example.test");
        _store.GetAsync(anna.Id, Arg.Any<CancellationToken>()).Returns(new Versioned<User>(anna, "etag"));

        var found = await new TableUserDirectory(_store).FindMemberAsync(anna.Id, CancellationToken.None);

        Assert.Equal(new DirectoryUser(anna.Id, "Anna", "a@example.test"), found);
    }

    [Fact]
    public async Task FindMemberAsync_Unknown_ReturnsNull()
    {
        _store.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Versioned<User>?)null);

        var found = await new TableUserDirectory(_store).FindMemberAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(found);
    }
}
