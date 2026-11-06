// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Transfers;
using SharpVault.Domain.Users;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Application.Transfers.Job;

[Trait("Category", "Security")]
public sealed class TransferGateTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IUserStore _users = Substitute.For<IUserStore>();
    private readonly IVaultStore _vaults = Substitute.For<IVaultStore>();
    private readonly TransferGate _gate;
    private readonly Guid _requester = Guid.NewGuid();
    private readonly Guid _approver = Guid.NewGuid();
    private readonly Guid _source = Guid.NewGuid();
    private readonly Guid _target = Guid.NewGuid();

    public TransferGateTests()
    {
        _gate = new TransferGate(_users, _vaults);
        _vaults.ListInventoryAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VaultInventoryPage([], null)));
        _vaults.ListFoldersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<Folder>>>([]));
        SetUser(_requester, Admin());
        SetUser(_approver, Admin());
        SetUser(_source, Person(UserStatus.Active));
        SetUser(_target, Person(UserStatus.Active));
    }

    private static User Admin(UserStatus status = UserStatus.Active, bool isAdmin = true) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Admin",
        Status = status,
        IsAdmin = isAdmin,
        CreatedAt = Start,
        LastSignInAt = Start,
    };

    private static User Person(UserStatus status) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "Person",
        Status = status,
        CreatedAt = Start,
        LastSignInAt = Start,
    };

    private void SetUser(Guid id, User? user)
    {
        var stored = user is null ? null : new Versioned<User>(user, "etag-user");
        _users.GetAsync(id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(stored));
    }

    private TransferJob Job(ReasonCategory category = ReasonCategory.RoleChange, bool decided = true) => new()
    {
        Id = Guid.NewGuid(),
        State = TransferState.Approved,
        RequestedBy = _requester,
        DecidedBy = decided ? _approver : null,
        SourceUserId = _source,
        TargetUserId = _target,
        Category = category,
        ScopeKind = TransferScopeKind.AllItems,
    };

    private void TargetFolders(int count) =>
        _vaults.ListFoldersAsync(_target, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Versioned<Folder>>>(Enumerable.Range(0, count)
                .Select(_ => new Versioned<Folder>(new Folder(), "etag-folder"))
                .ToList()));

    [Fact]
    public async Task CheckAsync_HealthyParties_ReturnsNoBlock()
    {
        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: true, TestContext.Current.CancellationToken);

        Assert.Null(reason);
    }

    [Fact]
    public async Task CheckAsync_RequesterMissing_ReturnsRequesterNoLongerAdmin()
    {
        SetUser(_requester, null);

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.RequesterNoLongerAdmin, reason);
    }

    [Fact]
    public async Task CheckAsync_RequesterNoLongerAdmin_ReturnsRequesterNoLongerAdmin()
    {
        SetUser(_requester, Admin(isAdmin: false));

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.RequesterNoLongerAdmin, reason);
    }

    [Fact]
    public async Task CheckAsync_RequesterLocked_ReturnsRequesterNoLongerAdmin()
    {
        SetUser(_requester, Admin(status: UserStatus.Locked));

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.RequesterNoLongerAdmin, reason);
    }

    [Fact]
    public async Task CheckAsync_RequesterAndTargetBothBlocked_ReportsTheRequesterFirst()
    {
        SetUser(_requester, Admin(isAdmin: false));
        SetUser(_target, Person(UserStatus.Locked));

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.RequesterNoLongerAdmin, reason);
    }

    [Fact]
    public async Task CheckAsync_ApproverNoLongerAdmin_ReturnsApproverNoLongerAdmin()
    {
        SetUser(_approver, Admin(isAdmin: false));

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.ApproverNoLongerAdmin, reason);
    }

    [Fact]
    public async Task CheckAsync_ApproverLocked_ReturnsApproverNoLongerAdmin()
    {
        SetUser(_approver, Admin(status: UserStatus.Locked));

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.ApproverNoLongerAdmin, reason);
    }

    [Fact]
    public async Task CheckAsync_ApproverMissing_ReturnsApproverNoLongerAdmin()
    {
        SetUser(_approver, null);

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.ApproverNoLongerAdmin, reason);
    }

    [Fact]
    public async Task CheckAsync_NoApproverRecorded_ReturnsApproverNoLongerAdmin()
    {
        var reason = await _gate.CheckAsync(Job(decided: false), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.ApproverNoLongerAdmin, reason);
    }

    [Fact]
    public async Task CheckAsync_SourceMissing_ReturnsSourceMissing()
    {
        SetUser(_source, null);

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.SourceMissing, reason);
    }

    [Theory]
    [InlineData(ReasonCategory.LeftCompany)]
    [InlineData(ReasonCategory.Dismissed)]
    [InlineData(ReasonCategory.Deceased)]
    public async Task CheckAsync_ActiveSourceForALockedCategory_ReturnsSourceNotLocked(ReasonCategory category)
    {
        var reason = await _gate.CheckAsync(Job(category), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.SourceNotLocked, reason);
    }

    [Theory]
    [InlineData(ReasonCategory.LeftCompany)]
    [InlineData(ReasonCategory.Dismissed)]
    [InlineData(ReasonCategory.Deceased)]
    public async Task CheckAsync_LockedSourceForALockedCategory_IsNotBlockedBySource(ReasonCategory category)
    {
        SetUser(_source, Person(UserStatus.Locked));

        var reason = await _gate.CheckAsync(Job(category), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Null(reason);
    }

    [Theory]
    [InlineData(ReasonCategory.RoleChange)]
    [InlineData(ReasonCategory.Other)]
    public async Task CheckAsync_ActiveSourceForANonLockedCategory_IsNotBlockedBySource(ReasonCategory category)
    {
        var reason = await _gate.CheckAsync(Job(category), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Null(reason);
    }

    [Fact]
    public async Task CheckAsync_TargetMissing_ReturnsTargetMissing()
    {
        SetUser(_target, null);

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.TargetMissing, reason);
    }

    [Fact]
    public async Task CheckAsync_TargetLocked_ReturnsTargetLocked()
    {
        SetUser(_target, Person(UserStatus.Locked));

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.TargetLocked, reason);
    }

    [Fact]
    public async Task CheckAsync_TargetAtTheFolderLimit_ReturnsTargetFolderLimit()
    {
        TargetFolders(VaultLimits.MaxFoldersPerVault);

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: true, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.TargetFolderLimit, reason);
    }

    [Fact]
    public async Task CheckAsync_TargetOneBelowTheFolderLimit_ReturnsNoBlockWhenNoFolderIsMoved()
    {
        TargetFolders(VaultLimits.MaxFoldersPerVault - 1);

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: true, TestContext.Current.CancellationToken);

        Assert.Null(reason);
    }

    [Fact]
    public async Task CheckAsync_InScopeFoldersCountTowardTheFolderLimit_ReturnsTargetFolderLimit()
    {
        _vaults.ListInventoryAsync(_source, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VaultInventoryPage(
                [new VaultInventoryRow(VaultItemKind.ActiveFolder, Guid.NewGuid(), null, Start, "etag-row")],
                null)));
        TargetFolders(VaultLimits.MaxFoldersPerVault - 1);

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: true, TestContext.Current.CancellationToken);

        Assert.Equal(TransferBlockReason.TargetFolderLimit, reason);
    }

    [Fact]
    public async Task CheckAsync_FolderLimitNotChecked_IgnoresAFullTarget()
    {
        TargetFolders(VaultLimits.MaxFoldersPerVault);

        var reason = await _gate.CheckAsync(Job(), checkFolderLimit: false, TestContext.Current.CancellationToken);

        Assert.Null(reason);
    }

    [Fact]
    public async Task CheckAsync_NullJob_ThrowsArgumentNullException() =>
        await Assert.ThrowsAsync<ArgumentNullException>(() => _gate.CheckAsync(null!, checkFolderLimit: false, TestContext.Current.CancellationToken));
}
