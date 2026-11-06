// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Sharing;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Sharing;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Sharing.Common;

[Trait("Category", "Security")]
public sealed class ShareGrantServiceTests
{
    private readonly List<string> _calls = [];
    private readonly InMemoryShareInbox _inbox;
    private readonly RecordingLogger<ShareGrantService> _logger = new();
    private readonly IVaultStore _store = Substitute.For<IVaultStore>();

    public ShareGrantServiceTests()
    {
        _inbox = new InMemoryShareInbox(_calls);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddAsync_NullGrant_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => NewService().AddAsync(null!, null, Token));
    }

    [Fact]
    public async Task AddAsync_Grant_WritesTheInboxRowBeforeTheVaultGrant()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        ArrangeSubmit(StoreWriteResult.Written);

        await NewService().AddAsync(grant, null, Token);

        Assert.Equal(["inbox.add", "vault.submit"], _calls.Where(call => call != "inbox.get").Take(2));
    }

    [Fact]
    public async Task AddAsync_FiledFolder_IsCarriedOnTheInboxRow()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        var filed = Guid.NewGuid();
        ArrangeSubmit(StoreWriteResult.Written);

        await NewService().AddAsync(grant, filed, Token);

        var row = Assert.Single(_inbox.Rows);
        Assert.Equal(filed, row.FiledFolderId);
        Assert.Equal(grant.RecipientId, row.RecipientId);
        Assert.Equal(grant.OwnerId, row.OwnerId);
        Assert.Equal(grant.ItemId, row.ItemId);
        Assert.Equal(grant.ItemKind, row.ItemKind);
    }

    [Fact]
    public async Task AddAsync_Grant_SubmitsOneAddShareGrantChangeToTheOwnerPartition()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        ArrangeSubmit(StoreWriteResult.Written);

        await NewService().AddAsync(grant, null, Token);

        await _store.Received(1).SubmitAsync(
            grant.OwnerId,
            Arg.Is<IReadOnlyList<VaultChange>>(changes => changes.Count == 1 && changes[0].Kind == VaultChangeKind.AddShareGrant && changes[0].Grant == grant),
            Token);
    }

    [Theory]
    [InlineData(StoreWriteResult.Written)]
    [InlineData(StoreWriteResult.AlreadyExists)]
    [InlineData(StoreWriteResult.Conflict)]
    public async Task AddAsync_AnyStoreResult_ReturnsTheWriteResult(StoreWriteResult result)
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        ArrangeSubmit(result);

        var write = await NewService().AddAsync(grant, null, Token);

        Assert.Equal(result, write.Result);
    }

    [Fact]
    public async Task AddAsync_VaultWriteConflicts_DoesNotRecheckTheInboxRow()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        ArrangeSubmit(StoreWriteResult.Conflict);

        await NewService().AddAsync(grant, null, Token);

        Assert.Equal(1, _calls.Count(call => call == "inbox.add"));
    }

    [Fact]
    public async Task RevokeAsync_NoGrantRows_ReturnsNotFoundAndRemovesTheInboxRow()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        await _inbox.AddIfAbsentAsync(ShareSamples.InboxEntry(grant.RecipientId, grant.OwnerId, grant.ItemKind, grant.ItemId), Token);
        _store.GetShareGrantRowsAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ShareGrantRows?>(null));

        var outcome = await NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.Equal(ShareRevokeResult.NotFound, outcome.Result);
        Assert.Null(outcome.Revoked);
        Assert.Empty(_inbox.Rows);
        await _store.DidNotReceive().SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_Written_RemovesTheInboxRowAfterTheVaultDelete()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        await _inbox.AddIfAbsentAsync(ShareSamples.InboxEntry(grant.RecipientId, grant.OwnerId, grant.ItemKind, grant.ItemId), Token);
        _calls.Clear();
        ArrangeRows(grant);
        ArrangeSubmit(StoreWriteResult.Written);

        var outcome = await NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.Equal(ShareRevokeResult.Revoked, outcome.Result);
        Assert.Same(grant, outcome.Revoked);
        Assert.Equal(["vault.submit", "inbox.remove"], _calls);
        Assert.Empty(_inbox.Rows);
    }

    [Fact]
    public async Task RevokeAsync_Written_SubmitsTheDeleteWithBothETags()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        ArrangeRows(grant);
        ArrangeSubmit(StoreWriteResult.Written);

        await NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        await _store.Received(1).SubmitAsync(
            grant.OwnerId,
            Arg.Is<IReadOnlyList<VaultChange>>(changes =>
                changes.Count == 1
                && changes[0].Kind == VaultChangeKind.DeleteShareGrant
                && changes[0].ETag == "gi"
                && changes[0].SecondaryETag == "gr"),
            Token);
    }

    [Fact]
    public async Task RevokeAsync_ConflictThenWritten_RereadsAndRevokes()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        ArrangeRows(grant);
        var results = new Queue<StoreWriteResult>([StoreWriteResult.Conflict, StoreWriteResult.Written]);
        _store.SubmitAsync(grant.OwnerId, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new VaultWriteResult(results.Dequeue(), 0, [])));

        var outcome = await NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.Equal(ShareRevokeResult.Revoked, outcome.Result);
        await _store.Received(2).GetShareGrantRowsAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_EveryAttemptConflicts_ReturnsConflictKeepsTheInboxRowAndStopsAtTheAttemptLimit()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        await _inbox.AddIfAbsentAsync(ShareSamples.InboxEntry(grant.RecipientId, grant.OwnerId, grant.ItemKind, grant.ItemId), Token);
        ArrangeRows(grant);
        ArrangeSubmit(StoreWriteResult.Conflict);

        var outcome = await NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.Equal(ShareRevokeResult.Conflict, outcome.Result);
        Assert.Null(outcome.Revoked);
        Assert.Single(_inbox.Rows);
        await _store.Received(SharingLimits.RevokeAttempts).SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_PartialWriteInterruption_ThrowsAndKeepsTheInboxRow()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        await _inbox.AddIfAbsentAsync(ShareSamples.InboxEntry(grant.RecipientId, grant.OwnerId, grant.ItemKind, grant.ItemId), Token);
        ArrangeRows(grant);
        var interruption = ExceptionDispatchInfo.Capture(new InvalidOperationException("storage failed"));
        _store.SubmitAsync(grant.OwnerId, Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VaultWriteResult(StoreWriteResult.Conflict, 0, [], interruption)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Token));

        Assert.Single(_inbox.Rows);
    }

    [Fact]
    public async Task RevokeAsync_InboxRemovalFailsAfterWrite_StillReportsRevokedAndLogsOnlyTheExceptionType()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        ArrangeRows(grant);
        ArrangeSubmit(StoreWriteResult.Written);
        _inbox.RemoveFailure = new InvalidOperationException("secret-detail-12345");

        var outcome = await NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.Equal(ShareRevokeResult.Revoked, outcome.Result);
        var record = Assert.Single(_logger.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains(nameof(InvalidOperationException), record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-detail-12345", _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeAsync_InboxRemovalFailsForMissingGrant_StillReportsNotFound()
    {
        var grant = ShareSamples.Grant(Guid.NewGuid());
        _store.GetShareGrantRowsAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ShareGrantRows?>(null));
        _inbox.RemoveFailure = new InvalidOperationException("boom");

        var outcome = await NewService().RevokeAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Token);

        Assert.Equal(ShareRevokeResult.NotFound, outcome.Result);
        Assert.Contains(_logger.Records, record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task RevokeAllReceivedAsync_PagesAndReturnsRevoked()
    {
        var recipient = Guid.NewGuid();
        var grants = Enumerable.Range(0, 5).Select(_ => ShareSamples.Grant(Guid.NewGuid(), recipient: recipient)).ToList();
        foreach (var grant in grants)
        {
            await _inbox.AddIfAbsentAsync(ShareSamples.InboxEntry(recipient, grant.OwnerId, grant.ItemKind, grant.ItemId), Token);
            ArrangeRows(grant);
        }

        _inbox.PageSizeCap = 2;
        ArrangeSubmit(StoreWriteResult.Written);

        var outcome = await NewService().RevokeAllReceivedAsync(recipient, Token);

        Assert.True(outcome.IsComplete);
        Assert.Equal(5, outcome.RevokedCount);
        Assert.Empty(_inbox.Rows);
        Assert.Equal(3, _inbox.RequestedPageSizes.Count);
        Assert.All(_inbox.RequestedPageSizes, size => Assert.Equal(SharingLimits.MaxPageSize, size));
    }

    [Fact]
    public async Task RevokeAllReceivedAsync_RepeatAfterFailure_FinishesTheRest()
    {
        var recipient = Guid.NewGuid();
        var grants = Enumerable.Range(0, 3).Select(_ => ShareSamples.Grant(Guid.NewGuid(), recipient: recipient)).ToList();
        foreach (var grant in grants)
        {
            await _inbox.AddIfAbsentAsync(ShareSamples.InboxEntry(recipient, grant.OwnerId, grant.ItemKind, grant.ItemId), Token);
            ArrangeRows(grant);
        }

        var failing = grants[1].OwnerId;
        var failureOn = true;
        _store.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new VaultWriteResult(
                failureOn && call.ArgAt<Guid>(0) == failing ? StoreWriteResult.Conflict : StoreWriteResult.Written,
                0,
                [])));

        var first = await NewService().RevokeAllReceivedAsync(recipient, Token);
        failureOn = false;
        var second = await NewService().RevokeAllReceivedAsync(recipient, Token);

        Assert.False(first.IsComplete);
        Assert.Equal(2, first.RevokedCount);
        Assert.True(second.IsComplete);
        Assert.Equal(1, second.RevokedCount);
        Assert.Empty(_inbox.Rows);
    }

    [Fact]
    public async Task RevokeAllReceivedAsync_RowWithoutGrant_IsDroppedAndStaysComplete()
    {
        var recipient = Guid.NewGuid();
        await _inbox.AddIfAbsentAsync(ShareSamples.InboxEntry(recipient), Token);
        _store.GetShareGrantRowsAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), recipient, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ShareGrantRows?>(null));

        var outcome = await NewService().RevokeAllReceivedAsync(recipient, Token);

        Assert.True(outcome.IsComplete);
        Assert.Equal(0, outcome.RevokedCount);
        Assert.Empty(_inbox.Rows);
        Assert.DoesNotContain(_logger.Records, record => record.Level == LogLevel.Error);
    }

    [Fact]
    public async Task RevokeAllReceivedAsync_ContinuationTokenDoesNotAdvance_IsIncompleteAndLogsError()
    {
        var recipient = Guid.NewGuid();
        var inbox = Substitute.For<IShareInboxStore>();
        var calls = 0;
        inbox.ListAsync(recipient, null, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ShareInboxPage(
                calls++ == 0 ? [new Versioned<ShareInboxEntry>(ShareSamples.InboxEntry(recipient), "e")] : [],
                "same")));
        _store.GetShareGrantRowsAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), recipient, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ShareGrantRows?>(null));

        var outcome = await new ShareGrantService(_store, inbox, TimeProvider.System, _logger).RevokeAllReceivedAsync(recipient, Token);

        Assert.False(outcome.IsComplete);
        Assert.Contains(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RevokeAllReceivedAsync_OnlyOtherRecipientsRows_RevokesNothing()
    {
        var recipient = Guid.NewGuid();
        await _inbox.AddIfAbsentAsync(ShareSamples.InboxEntry(Guid.NewGuid()), Token);

        var outcome = await NewService().RevokeAllReceivedAsync(recipient, Token);

        Assert.True(outcome.IsComplete);
        Assert.Equal(0, outcome.RevokedCount);
        Assert.Single(_inbox.Rows);
        await _store.DidNotReceive().SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>());
    }

    private ShareGrantService NewService() => new(_store, _inbox, TimeProvider.System, _logger);

    private void ArrangeSubmit(StoreWriteResult result) =>
        _store.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _calls.Add("vault.submit");
                return Task.FromResult(new VaultWriteResult(result, result == StoreWriteResult.Written ? 1 : 0, []));
            });

    private void ArrangeRows(ShareGrant grant) =>
        _store.GetShareGrantRowsAsync(grant.OwnerId, grant.ItemKind, grant.ItemId, grant.RecipientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ShareGrantRows?>(new ShareGrantRows(new Versioned<ShareGrant>(grant, "gi"), "gr")));
}
