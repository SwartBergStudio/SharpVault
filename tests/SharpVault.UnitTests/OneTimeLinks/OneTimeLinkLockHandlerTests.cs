// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Commands;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Users.Commands;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Domain.Users;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
[Collection(MapsterGlobalSettings.Name)]
public sealed class OneTimeLinkLockHandlerTests
{
    private const string ClientIp = "203.0.113.9";
    private const string UserAgent = "Agent/1.0";
    private const string Reason = "Left the company";

    private readonly Dictionary<Guid, User> _userRows = [];
    private readonly IUserStore _users = Substitute.For<IUserStore>();
    private readonly IUserAccessCache _cache = Substitute.For<IUserAccessCache>();
    private readonly IShareInboxStore _inbox = Substitute.For<IShareInboxStore>();
    private readonly IVaultStore _vault = Substitute.For<IVaultStore>();
    private readonly OneTimeLinkMemoryStore _links = new();
    private readonly RecordingAuditLog _auditLog = new();
    private readonly FakeTimeProvider _time = new(OneTimeLinkSamples.Now);
    private readonly StubRequestMetadata _metadata = new();
    private readonly StubCurrentUser _admin = new(Guid.NewGuid(), isAdmin: true);
    private readonly Guid _target = Guid.NewGuid();

    public OneTimeLinkLockHandlerTests()
    {
        GlobalMapping.EnsureApplicationMappings();
        AddUser(_admin.UserId, UserStatus.Active, isAdmin: true);
        _users.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(StoredUser(call.Arg<Guid>())));
        _users.ReplaceAsync(Arg.Any<User>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var user = call.Arg<User>();
            _userRows[user.Id] = user;
            return Task.FromResult(StoreWriteResult.Written);
        });
        _inbox.ListAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ShareInboxPage([], null)));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LockUserCommandHandler LockHandler() => new(
        new AdminActorGuard(_admin, _users),
        new UserStatusWriter(_users, _cache),
        new AdminAuditWriter(_auditLog, _time, NullLogger<AdminAuditWriter>.Instance),
        new VaultAuditWriter(_admin, _metadata, _auditLog, _time, NullLogger<VaultAuditWriter>.Instance),
        new ShareGrantService(_vault, _inbox, _time, NullLogger<ShareGrantService>.Instance),
        _users,
        _metadata,
        Revoker());

    private UnlockUserCommandHandler UnlockHandler() => new(
        new AdminActorGuard(_admin, _users),
        new UserStatusWriter(_users, _cache),
        new AdminAuditWriter(_auditLog, _time, NullLogger<AdminAuditWriter>.Instance),
        _metadata,
        _users,
        Revoker());

    private OpenOneTimeLinkCommandHandler OpenHandler() => new(
        OneTimeLinkSamples.Policy(),
        _links,
        _users,
        OneTimeLinkSamples.AuditWriter(_auditLog, _time),
        _time,
        NullLogger<OpenOneTimeLinkCommandHandler>.Instance);

    private OneTimeLinkRevoker Revoker() => new(_links, OneTimeLinkSamples.AuditWriter(_auditLog, _time));

    private Versioned<User>? StoredUser(Guid userId) =>
        _userRows.TryGetValue(userId, out var user) ? new Versioned<User>(user, "user-etag") : null;

    private void AddUser(Guid userId, UserStatus status, bool isAdmin = false) => _userRows[userId] = new User
    {
        Id = userId,
        DisplayName = "Test user",
        Status = status,
        IsAdmin = isAdmin,
        CreatedAt = OneTimeLinkSamples.Now,
        LastSignInAt = OneTimeLinkSamples.Now,
    };

    private OneTimeLink GivenLink(Guid creatorId)
    {
        var link = OneTimeLinkSamples.NewLink(creatorId, Guid.NewGuid(), Guid.NewGuid(), OneTimeLinkSamples.Now.AddDays(1));
        _links.Add(link, OneTimeLinkSamples.IndexFor(link, [1]));
        return link;
    }

    private int Count(AuditEventType eventType) => _auditLog.Entries.Count(entry => entry.EventType == eventType);

    private static LockUserCommand Lock(Guid userId, ReasonCategory category) => new(userId, category, Reason);

    [Theory]
    [InlineData(ReasonCategory.LeftCompany)]
    [InlineData(ReasonCategory.Dismissed)]
    [InlineData(ReasonCategory.Deceased)]
    [InlineData(ReasonCategory.RoleChange)]
    [InlineData(ReasonCategory.Other)]
    public async Task Lock_EveryCategory_RevokesActiveLinksAndAuditsEach(ReasonCategory category)
    {
        AddUser(_target, UserStatus.Active);
        GivenLink(_target);
        GivenLink(_target);
        GivenLink(Guid.NewGuid());

        var result = await LockHandler().Handle(Lock(_target, category), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Locked, _userRows[_target].Status);
        Assert.Equal(2, Count(AuditEventType.OneTimeLinkRevoked));
        Assert.Equal(1, _links.ActiveLinkCount);
    }

    [Fact]
    public async Task Lock_Dismissed_RevokesLinksAndSweepsReceivedShares()
    {
        AddUser(_target, UserStatus.Active);
        GivenLink(_target);

        var result = await LockHandler().Handle(Lock(_target, ReasonCategory.Dismissed), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _links.ActiveLinkCount);
        Assert.Equal(1, Count(AuditEventType.OneTimeLinkRevoked));
        await _inbox.Received().ListAsync(_target, null, Arg.Any<int>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Lock_SweepFailsMidway_Returns500LockStaysAndTheLinkDoesNotOpen()
    {
        AddUser(_target, UserStatus.Active);
        var link = GivenLink(_target);
        _links.FailListIndexOnCall = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => LockHandler().Handle(Lock(_target, ReasonCategory.LeftCompany), Token));

        Assert.Equal(UserStatus.Locked, _userRows[_target].Status);
        Assert.Equal(1, Count(AuditEventType.UserLocked));
        var opened = await OpenHandler().Handle(new OpenOneTimeLinkCommand(link.Id, ClientIp, UserAgent), Token);
        Assert.Equal(ResultFailureKind.NotFound, opened.FailureKind);
        Assert.Equal(1, _links.ActiveLinkCount);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Lock_RepeatAfterFailedSweep_FinishesForANonDismissedCategory()
    {
        AddUser(_target, UserStatus.Active);
        GivenLink(_target);
        _links.FailListIndexOnCall = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => LockHandler().Handle(Lock(_target, ReasonCategory.LeftCompany), Token));
        _links.FailListIndexOnCall = null;

        var repeat = await LockHandler().Handle(Lock(_target, ReasonCategory.LeftCompany), Token);

        Assert.Equal(ResultFailureKind.Rejected, repeat.FailureKind);
        Assert.Equal(UserAdminMessages.AlreadyLocked, repeat.Error);
        Assert.Equal(0, _links.ActiveLinkCount);
        Assert.Equal(1, Count(AuditEventType.OneTimeLinkRevoked));
        Assert.Equal(1, Count(AuditEventType.UserLocked));
    }

    [Fact]
    public async Task Lock_RepeatWithNoLinks_Returns400WithNoSecondLockRow()
    {
        AddUser(_target, UserStatus.Locked);

        var result = await LockHandler().Handle(Lock(_target, ReasonCategory.LeftCompany), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(UserAdminMessages.AlreadyLocked, result.Error);
        Assert.Equal(0, Count(AuditEventType.UserLocked));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Unlock_AfterPartialSweep_LinkDoesNotOpen()
    {
        AddUser(_target, UserStatus.Locked);
        var first = GivenLink(_target);
        var second = GivenLink(_target);
        _links.FailConsumeOnCall = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => UnlockHandler().Handle(new UnlockUserCommand(_target, ReasonCategory.Other, Reason), Token));

        Assert.Equal(UserStatus.Locked, _userRows[_target].Status);
        foreach (var link in new[] { first, second })
        {
            var opened = await OpenHandler().Handle(new OpenOneTimeLinkCommand(link.Id, ClientIp, UserAgent), Token);
            Assert.Equal(ResultFailureKind.NotFound, opened.FailureKind);
        }
    }

    [Fact]
    public async Task Unlock_LockedUserWithLinks_RevokesThemBeforeActivating()
    {
        AddUser(_target, UserStatus.Locked);
        GivenLink(_target);

        var result = await UnlockHandler().Handle(new UnlockUserCommand(_target, ReasonCategory.Other, Reason), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Active, _userRows[_target].Status);
        Assert.Equal(0, _links.ActiveLinkCount);
        Assert.Equal(1, Count(AuditEventType.OneTimeLinkRevoked));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Unlock_ActiveUser_Returns400AndKeepsTheirLinks()
    {
        AddUser(_target, UserStatus.Active);
        GivenLink(_target);

        var result = await UnlockHandler().Handle(new UnlockUserCommand(_target, ReasonCategory.Other, Reason), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(UserAdminMessages.AlreadyActive, result.Error);
        Assert.Equal(1, _links.ActiveLinkCount);
        Assert.Equal(0, _links.ListIndexCalls);
    }
}
