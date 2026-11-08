// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkRevokerTests
{
    private const int RevokeAllPageSize = 50;

    private readonly OneTimeLinkMemoryStore _store = new();
    private readonly RecordingAuditLog _auditLog = new();
    private readonly FakeTimeProvider _time = new(OneTimeLinkSamples.Now);
    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _creator = Guid.NewGuid();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private OneTimeLinkRevoker Revoker() => new(_store, OneTimeLinkSamples.AuditWriter(_auditLog, _time));

    private OneTimeLink AddLink(Guid creatorId)
    {
        var link = OneTimeLinkSamples.NewLink(creatorId, Guid.NewGuid(), Guid.NewGuid(), OneTimeLinkSamples.Now.AddDays(1));
        _store.Add(link, OneTimeLinkSamples.IndexFor(link, [1]));
        return link;
    }

    private static OneTimeLinkIndexEntry IndexOf(OneTimeLink link) => OneTimeLinkSamples.IndexFor(link, [1]);

    [Fact]
    public async Task RevokeAsync_LiveLink_AuditsRevokedAndRemovesBookkeeping()
    {
        var link = AddLink(_creator);

        var revoked = await Revoker().RevokeAsync(IndexOf(link), _actor, "198.51.100.4", Token);

        Assert.True(revoked);
        Assert.Equal(0, _store.ActiveLinkCount);
        Assert.Equal(0, _store.IndexCount);
        var entry = Assert.Single(_auditLog.Entries);
        Assert.Equal(AuditEventType.OneTimeLinkRevoked, entry.EventType);
        Assert.Equal(_actor, entry.ActorId);
        Assert.Equal("198.51.100.4", entry.IpAddress);
    }

    [Fact]
    public async Task RevokeAsync_LinkAlreadyGone_ReturnsFalseWithoutAuditButRemovesBookkeeping()
    {
        var link = OneTimeLinkSamples.NewLink(_creator, Guid.NewGuid(), Guid.NewGuid(), OneTimeLinkSamples.Now.AddDays(1));
        _store.AddIndexOnly(IndexOf(link));

        var revoked = await Revoker().RevokeAsync(IndexOf(link), _actor, null, Token);

        Assert.False(revoked);
        Assert.Equal(0, _store.IndexCount);
        Assert.Empty(_auditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RevokeAsync_AuditFails_PropagatesAndStillRemovesBookkeeping()
    {
        var link = AddLink(_creator);
        _auditLog.Failure = new InvalidOperationException("audit down");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Revoker().RevokeAsync(IndexOf(link), _actor, null, Token));

        Assert.Equal(0, _store.IndexCount);
    }

    [Fact]
    public async Task RevokeAsync_NullEntry_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Revoker().RevokeAsync(null!, _actor, null, Token));
    }

    [Fact]
    public async Task RevokeAllForCreatorAsync_MoreLinksThanOnePage_RevokesEveryLinkAndAuditsEach()
    {
        var mine = Enumerable.Range(0, RevokeAllPageSize + 5).Select(_ => AddLink(_creator)).ToList();
        var other = AddLink(Guid.NewGuid());

        await Revoker().RevokeAllForCreatorAsync(_creator, _actor, null, Token);

        Assert.Equal(1, _store.ActiveLinkCount);
        Assert.Equal(mine.Count, _auditLog.Entries.Count(entry => entry.EventType == AuditEventType.OneTimeLinkRevoked));
        Assert.DoesNotContain(_auditLog.Entries, entry => entry.TargetId == other.Id.ToString("D"));
    }

    [Fact]
    public async Task RevokeAllForCreatorAsync_NoLinks_ReturnsWithoutAudit()
    {
        await Revoker().RevokeAllForCreatorAsync(_creator, _actor, null, Token);

        Assert.Empty(_auditLog.Entries);
        Assert.Equal(1, _store.ListIndexCalls);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RevokeAll_StoreThrowsMidway_Propagates()
    {
        for (var index = 0; index < RevokeAllPageSize + 5; index++)
        {
            AddLink(_creator);
        }

        _store.FailListIndexOnCall = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Revoker().RevokeAllForCreatorAsync(_creator, _actor, null, Token));

        Assert.Equal(RevokeAllPageSize, _auditLog.Entries.Count(entry => entry.EventType == AuditEventType.OneTimeLinkRevoked));
        Assert.Equal(5, _store.ActiveLinkCount);
    }
}
