// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Users.Queries;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Users;

namespace SharpVault.UnitTests.Application.Users;

[Trait("Category", "Security")]
public sealed class GetUserCountsHandlerTests
{
    private const string ETag = "\"etag\"";
    private const int TwoActiveSecrets = 2;
    private const int ThreeShares = 3;
    private const int ExpectedItemCountWithRecycledRows = 2;

    private readonly Guid _adminId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserStore _users = Substitute.For<IUserStore>();
    private readonly IVaultStore _vaults = Substitute.For<IVaultStore>();

    public GetUserCountsHandlerTests()
    {
        _currentUser.UserId.Returns(_adminId);
        _currentUser.IsAdmin.Returns(true);
        _users.GetAsync(_adminId, Arg.Any<CancellationToken>()).Returns(new Versioned<User>(ActiveUser(admin: true), ETag));
        _users.GetAsync(_targetId, Arg.Any<CancellationToken>()).Returns(new Versioned<User>(ActiveUser(admin: false), ETag));
        _vaults.ListInventoryAsync(_targetId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new VaultInventoryPage([], null));
        _vaults.CountShareGrantsByOwnerAsync(_targetId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new StoredCount(0, false));
    }

    [Fact]
    public async Task Handle_NotAnAdmin_ReturnsForbidden()
    {
        _currentUser.IsAdmin.Returns(false);

        var result = await Handle(TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultFailureKind.Forbidden, result.FailureKind);
    }

    [Fact]
    public async Task Handle_UnknownUser_ReturnsNotFound()
    {
        _users.GetAsync(_targetId, Arg.Any<CancellationToken>()).Returns((Versioned<User>?)null);

        var result = await Handle(TestContext.Current.CancellationToken);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
    }

    [Fact]
    public async Task Handle_ActiveSecretsAndShares_ReturnsExactCountsIgnoringRecycledRowsAndFolders()
    {
        _vaults.ListInventoryAsync(_targetId, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            new VaultInventoryPage(
            [
                Row(VaultItemKind.ActiveSecret),
                Row(VaultItemKind.ActiveSecret),
                Row(VaultItemKind.RecycledSecret),
                Row(VaultItemKind.ActiveFolder),
            ],
            null));
        _vaults.CountShareGrantsByOwnerAsync(_targetId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new StoredCount(ThreeShares, false));

        var counts = Assert.IsType<UserCountsDto>((await Handle(TestContext.Current.CancellationToken)).Value);

        Assert.Equal(ExpectedItemCountWithRecycledRows, counts.ItemCount);
        Assert.Equal(ThreeShares, counts.ShareCount);
        Assert.False(counts.CountsAreCapped);
    }

    [Fact]
    public async Task Handle_InventoryOverThePageCap_ReportsCountsAreCapped()
    {
        _vaults.ListInventoryAsync(_targetId, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new VaultInventoryPage([Row(VaultItemKind.ActiveSecret)], "more"));

        var counts = Assert.IsType<UserCountsDto>((await Handle(TestContext.Current.CancellationToken)).Value);

        Assert.True(counts.CountsAreCapped);
        await _vaults.Received(TransferLimits.InventoryMaxPages).ListInventoryAsync(_targetId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GrantsOverThePageCap_ReportsCountsAreCapped()
    {
        _vaults.CountShareGrantsByOwnerAsync(_targetId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new StoredCount(TwoActiveSecrets, true));

        var counts = Assert.IsType<UserCountsDto>((await Handle(TestContext.Current.CancellationToken)).Value);

        Assert.Equal(TwoActiveSecrets, counts.ShareCount);
        Assert.True(counts.CountsAreCapped);
    }

    [Fact]
    public void GetUserCountsQueryValidator_EmptyId_Fails()
    {
        var result = new GetUserCountsQueryValidator().Validate(new GetUserCountsQuery(Guid.Empty));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void UserCountsDto_PropertiesAreOnlyTwoIntsAndOneBool()
    {
        var properties = typeof(UserCountsDto).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property.Name, property.PropertyType))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [("CountsAreCapped", typeof(bool)), ("ItemCount", typeof(int)), ("ShareCount", typeof(int))],
            properties);
    }

    private Task<Result<UserCountsDto>> Handle(CancellationToken cancellationToken)
    {
        var handler = new GetUserCountsQueryHandler(new AdminActorGuard(_currentUser, _users), _users, _vaults);
        return handler.Handle(new GetUserCountsQuery(_targetId), cancellationToken);
    }

    private static User ActiveUser(bool admin) => new()
    {
        Status = UserStatus.Active,
        IsAdmin = admin,
    };

    private static VaultInventoryRow Row(VaultItemKind kind) =>
        new(kind, Guid.NewGuid(), null, DateTimeOffset.UtcNow, ETag);
}
