// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Queries;
using SharpVault.Domain.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Users;

[Collection(MapsterGlobalSettings.Name)]
public sealed class ListUsersTests
{
    private readonly ListUsersQueryValidator _validator = new();
    private readonly IUserStore _store = Substitute.For<IUserStore>();

    public ListUsersTests()
    {
        GlobalMapping.EnsureApplicationMappings();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_PageSizeInRange_Passes(int pageSize)
    {
        var result = _validator.Validate(new ListUsersQuery(pageSize, null));

        Assert.True(result.IsValid);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Validate_PageSizeOutOfRange_FailsWithConstantMessage(int pageSize)
    {
        var result = _validator.Validate(new ListUsersQuery(pageSize, null));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(ListUsersQuery.PageSize), error.PropertyName);
        Assert.Equal("PageSize must be between 1 and 100.", error.ErrorMessage);
    }

    [Fact]
    public void Validate_ContinuationTokenNull_Passes()
    {
        var result = _validator.Validate(new ListUsersQuery(50, null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CanonicalGuidContinuationToken_Passes()
    {
        var result = _validator.Validate(new ListUsersQuery(50, Guid.NewGuid().ToString("D")));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_UppercaseCanonicalGuidContinuationToken_Passes()
    {
        var result = _validator.Validate(new ListUsersQuery(50, Guid.NewGuid().ToString("D").ToUpperInvariant()));

        Assert.True(result.IsValid);
    }

    public static TheoryData<string> InvalidContinuationTokens() =>
    [
        string.Empty,
        " ",
        "' or PartitionKey ne '",
        "{" + Guid.NewGuid().ToString("D") + "}",
        "(" + Guid.NewGuid().ToString("D") + ")",
        Guid.NewGuid().ToString("N"),
        " " + Guid.NewGuid().ToString("D") + " ",
        Guid.NewGuid().ToString("D") + "' or 'a' eq 'a",
        Guid.NewGuid().ToString("D")[..35],
        new string('a', 10_000),
        "\u0000",
        "../x",
        "00000000-0000-0000-0000-00000000000g",
        Guid.NewGuid().ToString("D") + "\n",
    ];

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(InvalidContinuationTokens))]
    public void Validate_InvalidContinuationToken_FailsWithoutEchoingInput(string token)
    {
        var result = _validator.Validate(new ListUsersQuery(50, token));

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(ListUsersQuery.ContinuationToken), error.PropertyName);
        Assert.Equal("ContinuationToken is not valid.", error.ErrorMessage);
        if (token.Length > 3)
        {
            Assert.DoesNotContain(token, error.ErrorMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Validate_BothFieldsInvalid_ReportsBothWithoutValues()
    {
        var result = _validator.Validate(new ListUsersQuery(-5, "' or 1 eq 1"));

        Assert.Equal(2, result.Errors.Count);
        Assert.All(result.Errors, error =>
        {
            Assert.DoesNotContain("-5", error.ErrorMessage, StringComparison.Ordinal);
            Assert.DoesNotContain("1 eq 1", error.ErrorMessage, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Handle_FirstPage_PassesNullCursorAndMapsUsers()
    {
        var user = NewUser(UserStatus.Active);
        _store.ListAsync(25, null, Arg.Any<CancellationToken>()).Returns(new UserListPage([user], null));

        var result = await Handler().Handle(new ListUsersQuery(25, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var summary = Assert.Single(result.Value!.Users);
        Assert.Equal(user.Id, summary.UserId);
        Assert.Equal(user.DisplayName, summary.DisplayName);
        Assert.Equal(user.Email, summary.Email);
        Assert.Equal("Active", summary.Status);
        Assert.Equal(user.LastSignInAt, summary.LastSignInAt);
        await _store.Received(1).ListAsync(25, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LockedUser_MapsStatusName()
    {
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new UserListPage([NewUser(UserStatus.Locked)], null));

        var result = await Handler().Handle(new ListUsersQuery(10, null), TestContext.Current.CancellationToken);

        Assert.Equal("Locked", Assert.Single(result.Value!.Users).Status);
    }

    [Fact]
    public async Task Handle_WithCursor_PassesParsedGuid()
    {
        var cursor = Guid.NewGuid();
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new UserListPage([], null));

        await Handler().Handle(new ListUsersQuery(10, cursor.ToString("D").ToUpperInvariant()), TestContext.Current.CancellationToken);

        await _store.Received(1).ListAsync(10, cursor, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PageWithNextCursor_ReturnsCanonicalContinuationToken()
    {
        var next = Guid.NewGuid();
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new UserListPage([NewUser(UserStatus.Active)], next));

        var result = await Handler().Handle(new ListUsersQuery(10, null), TestContext.Current.CancellationToken);

        Assert.Equal(next.ToString("D"), result.Value!.ContinuationToken);
    }

    [Fact]
    public async Task Handle_LastPage_ReturnsNullContinuationToken()
    {
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new UserListPage([NewUser(UserStatus.Active)], null));

        var result = await Handler().Handle(new ListUsersQuery(10, null), TestContext.Current.CancellationToken);

        Assert.Null(result.Value!.ContinuationToken);
    }

    [Fact]
    public async Task Handle_EmptyPage_ReturnsEmptyListAndNullToken()
    {
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new UserListPage([], null));

        var result = await Handler().Handle(new ListUsersQuery(10, null), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Users);
        Assert.Null(result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_PassesCancellationTokenToStore()
    {
        using var source = new CancellationTokenSource();
        _store.ListAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new UserListPage([], null));

        await Handler().Handle(new ListUsersQuery(10, null), source.Token);

        await _store.Received(1).ListAsync(10, null, source.Token);
    }

    private static User NewUser(UserStatus status) =>
        new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "Alice Example",
            Email = "alice@example.test",
            Status = status,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastSignInAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
        };

    private ListUsersQueryHandler Handler() => new(_store);
}
