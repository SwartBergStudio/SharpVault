// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Commands;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class RevokeOneTimeLinkCommandHandlerTests
{
    private readonly OneTimeLinkMemoryStore _store = new();
    private readonly RecordingAuditLog _auditLog = new();
    private readonly FakeTimeProvider _time = new(OneTimeLinkSamples.Now);
    private readonly StubCurrentUser _creator = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private RevokeOneTimeLinkCommandHandler Handler(ICurrentUser? caller = null) => new(
        _store,
        new OneTimeLinkRevoker(_store, OneTimeLinkSamples.AuditWriter(_auditLog, _time)),
        caller ?? _creator,
        new StubRequestMetadata());

    private OneTimeLink AddLink(Guid creatorId)
    {
        var link = OneTimeLinkSamples.NewLink(creatorId, Guid.NewGuid(), Guid.NewGuid(), OneTimeLinkSamples.Now.AddDays(1));
        _store.Add(link, OneTimeLinkSamples.IndexFor(link, [1]));
        return link;
    }

    [Fact]
    public async Task Handle_OwnLiveLink_RevokesAuditsAndReturnsDone()
    {
        var link = AddLink(_creator.UserId);

        var result = await Handler().Handle(new RevokeOneTimeLinkCommand(link.Id), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, _store.ActiveLinkCount);
        Assert.Equal(0, _store.IndexCount);
        var entry = Assert.Single(_auditLog.Entries);
        Assert.Equal(AuditEventType.OneTimeLinkRevoked, entry.EventType);
        Assert.Equal(link.Id.ToString("D"), entry.TargetId);
        Assert.Equal(_creator.UserId, entry.ActorId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_OtherUsersLink_ReturnsNotFoundAndTheLinkStillOpens()
    {
        var link = AddLink(Guid.NewGuid());

        var result = await Handler().Handle(new RevokeOneTimeLinkCommand(link.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(1, _store.ActiveLinkCount);
        Assert.Empty(_auditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AdminOnAnotherUsersLink_ReturnsNotFoundAndTheLinkStillOpens()
    {
        var link = AddLink(Guid.NewGuid());
        var admin = new StubCurrentUser(isAdmin: true);

        var result = await Handler(admin).Handle(new RevokeOneTimeLinkCommand(link.Id), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Equal(1, _store.ActiveLinkCount);
        Assert.Empty(_auditLog.Entries);
    }

    [Fact]
    public async Task Handle_UnknownLink_ReturnsNotFound()
    {
        var result = await Handler().Handle(new RevokeOneTimeLinkCommand(Guid.NewGuid()), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_auditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_RevokedTwice_SecondReturnsNotFoundAndOneAuditRow()
    {
        var link = AddLink(_creator.UserId);
        var handler = Handler();

        var first = await handler.Handle(new RevokeOneTimeLinkCommand(link.Id), Token);
        var second = await handler.Handle(new RevokeOneTimeLinkCommand(link.Id), Token);

        Assert.True(first.IsSuccess);
        Assert.Equal(ResultFailureKind.NotFound, second.FailureKind);
        Assert.Single(_auditLog.Entries);
    }
}
