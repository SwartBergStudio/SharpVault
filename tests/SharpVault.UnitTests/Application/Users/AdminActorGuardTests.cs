// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Domain.Users;

namespace SharpVault.UnitTests.Application.Users;

[Trait("Category", "Security")]
public sealed class AdminActorGuardTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserStore _store = Substitute.For<IUserStore>();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly AdminActorGuard _guard;

    public AdminActorGuardTests()
    {
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAdmin.Returns(true);
        _guard = new AdminActorGuard(_currentUser, _store);
    }

    [Fact]
    public async Task Require_TokenNotAdmin_ReturnsNull()
    {
        _currentUser.IsAdmin.Returns(false);
        RowIs(UserStatus.Active, isAdmin: true);

        var actor = await _guard.RequireAsync(TestContext.Current.CancellationToken);

        Assert.Null(actor);
        await _store.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Require_RowLocked_ReturnsNull()
    {
        RowIs(UserStatus.Locked, isAdmin: true);

        Assert.Null(await _guard.RequireAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Require_RowNotAdmin_ReturnsNull()
    {
        RowIs(UserStatus.Active, isAdmin: false);

        Assert.Null(await _guard.RequireAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Require_RowMissing_ReturnsNull()
    {
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<User>?>(null));

        Assert.Null(await _guard.RequireAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Require_ActiveAdmin_ReturnsActor()
    {
        RowIs(UserStatus.Active, isAdmin: true);

        var actor = await _guard.RequireAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new AdminActor(_userId, "Admin Example"), actor);
    }

    [Fact]
    public async Task Require_AlwaysReadsTheStore()
    {
        RowIs(UserStatus.Active, isAdmin: true);

        await _guard.RequireAsync(TestContext.Current.CancellationToken);
        await _guard.RequireAsync(TestContext.Current.CancellationToken);

        await _store.Received(2).GetAsync(_userId, Arg.Any<CancellationToken>());
    }

    private void RowIs(UserStatus status, bool isAdmin) =>
        _store.GetAsync(_userId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<User>?>(new Versioned<User>(
            new User
            {
                Id = _userId,
                DisplayName = "Admin Example",
                Status = status,
                IsAdmin = isAdmin,
            },
            "etag")));
}
