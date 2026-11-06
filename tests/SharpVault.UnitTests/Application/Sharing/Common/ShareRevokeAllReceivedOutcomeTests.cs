// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Sharing;

namespace SharpVault.UnitTests.Application.Sharing.Common;

[Trait("Category", "Security")]
public sealed class ShareRevokeAllReceivedOutcomeTests
{
    private readonly IShareInboxStore _inbox = Substitute.For<IShareInboxStore>();
    private readonly CapturingLogger _logger = new();
    private readonly Guid _recipientId = Guid.NewGuid();
    private readonly IVaultStore _store = Substitute.For<IVaultStore>();

    [Fact]
    public async Task RevokeAllReceivedAsync_EveryGrantRevoked_IsCompleteWithCount()
    {
        ArrangeInboxPage(Entry(), Entry());
        ArrangeEveryRevokeResult(StoreWriteResult.Written);

        var outcome = await NewService().RevokeAllReceivedAsync(_recipientId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsComplete);
        Assert.Equal(2, outcome.RevokedCount);
        Assert.DoesNotContain(LogLevel.Error, _logger.Levels);
    }

    [Fact]
    public async Task RevokeAllReceivedAsync_OneGrantLosesEveryRace_IsIncompleteAndLogsError()
    {
        ArrangeInboxPage(Entry());
        ArrangeEveryRevokeResult(StoreWriteResult.Conflict);

        var outcome = await NewService().RevokeAllReceivedAsync(_recipientId, TestContext.Current.CancellationToken);

        Assert.False(outcome.IsComplete);
        Assert.Equal(0, outcome.RevokedCount);
        Assert.Contains(LogLevel.Error, _logger.Levels);
    }

    [Fact]
    public async Task RevokeAllReceivedAsync_OnlyEmptyPagesWithTokens_StopsBoundedAndIsIncomplete()
    {
        var pages = 0;
        _inbox.ListAsync(_recipientId, null, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ShareInboxPage([], $"token-{pages++}")));

        var outcome = await NewService().RevokeAllReceivedAsync(_recipientId, TestContext.Current.CancellationToken);

        Assert.False(outcome.IsComplete);
        Assert.Equal(SharingLimits.MaxEmptyInboxPagesPerCall, pages);
        Assert.Contains(LogLevel.Error, _logger.Levels);
    }

    [Fact]
    public async Task RevokeAllReceivedAsync_EmptyInbox_IsComplete()
    {
        ArrangeInboxPage();

        var outcome = await NewService().RevokeAllReceivedAsync(_recipientId, TestContext.Current.CancellationToken);

        Assert.True(outcome.IsComplete);
        Assert.Equal(0, outcome.RevokedCount);
    }

    private ShareGrantService NewService() => new(_store, _inbox, TimeProvider.System, _logger);

    private ShareInboxEntry Entry() => new()
    {
        RecipientId = _recipientId,
        OwnerId = Guid.NewGuid(),
        ItemKind = ShareItemKind.Secret,
        ItemId = Guid.NewGuid(),
    };

    private void ArrangeInboxPage(params ShareInboxEntry[] entries) =>
        _inbox.ListAsync(_recipientId, null, Arg.Any<int>(), null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ShareInboxPage([.. entries.Select(entry => new Versioned<ShareInboxEntry>(entry, "e"))], null)));

    private void ArrangeEveryRevokeResult(StoreWriteResult result)
    {
        _store.GetShareGrantRowsAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), _recipientId, Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<ShareGrantRows?>(new ShareGrantRows(
                new Versioned<ShareGrant>(
                    new ShareGrant
                    {
                        OwnerId = call.ArgAt<Guid>(0),
                        ItemKind = call.ArgAt<ShareItemKind>(1),
                        ItemId = call.ArgAt<Guid>(2),
                        RecipientId = _recipientId,
                        Permission = SharePermission.View,
                    },
                    "gi"),
                "gr")));
        _store.SubmitAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyList<VaultChange>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new VaultWriteResult(result, result == StoreWriteResult.Written ? 1 : 0, [])));
    }

    private sealed class CapturingLogger : ILogger<ShareGrantService>
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }
}
