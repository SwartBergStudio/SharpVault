// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Commands;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Domain.Users;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OpenOneTimeLinkCommandHandlerTests
{
    private const string ClientIp = "203.0.113.9";
    private const string UserAgent = "Agent/1.0";

    private readonly IOneTimeLinkStore _links = Substitute.For<IOneTimeLinkStore>();
    private readonly IUserStore _users = Substitute.For<IUserStore>();
    private readonly RecordingAuditLog _auditLog = new();
    private readonly FakeTimeProvider _time = new(OneTimeLinkSamples.Now);
    private readonly RecordingLogger<OpenOneTimeLinkCommandHandler> _logger = new();
    private readonly Guid _creatorId = Guid.NewGuid();
    private readonly OneTimeLink _link;

    public OpenOneTimeLinkCommandHandlerTests()
    {
        _link = OneTimeLinkSamples.NewLink(_creatorId, Guid.NewGuid(), Guid.NewGuid(), OneTimeLinkSamples.Now.AddDays(1));
        GivenLinkStored(_link);
        GivenCreator(UserStatus.Active);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private OpenOneTimeLinkCommandHandler Handler(IOneTimeLinkPolicy? policy = null) => new(
        policy ?? OneTimeLinkSamples.Policy(),
        _links,
        _users,
        OneTimeLinkSamples.AuditWriter(_auditLog, _time),
        _time,
        _logger);

    private OpenOneTimeLinkCommand Command(Guid? linkId = null, string? ip = ClientIp, string? agent = UserAgent) =>
        new(linkId ?? _link.Id, ip, agent);

    private void GivenLinkStored(OneTimeLink link)
    {
        _links.GetAsync(link.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<OneTimeLink>?>(new Versioned<OneTimeLink>(link, "link-etag")));
        _links.TryConsumeAsync(link.Id, "link-etag", Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
        _links.GetIndexEntryAsync(link.CreatorId, link.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<OneTimeLinkIndexEntry?>(OneTimeLinkSamples.IndexFor(link, [1, 2, 3])));
    }

    private void GivenCreator(UserStatus status) =>
        _users.GetAsync(_creatorId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<User>?>(OneTimeLinkSamples.UserRow(_creatorId, status)));

    [Fact]
    public async Task Handle_Success_ReturnsThePayloadAndAuditsOpened()
    {
        var result = await Handler().Handle(Command(), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(Convert.ToBase64String(_link.Payload), result.Value!.Payload);
        var entry = Assert.Single(_auditLog.Entries);
        Assert.Equal(AuditEventType.OneTimeLinkOpened, entry.EventType);
        Assert.Equal(AuditActors.AnonymousLinkRecipient, entry.ActorId);
        Assert.Equal(_link.Id.ToString("D"), entry.TargetId);
        Assert.Equal(_link.OwnerId, entry.OwnerId);
        Assert.Equal(_link.SecretId, entry.SecretId);
        Assert.Equal(ClientIp, entry.IpAddress);
        Assert.Equal(UserAgent, entry.UserAgent);
    }

    [Fact]
    [SuppressMessage("Usage", "CA2012", Justification = "The ValueTask is consumed by NSubstitute as the configured return value, not awaited by the test.")]
    public async Task Handle_ChecksRunInTheContractOrder()
    {
        var calls = new List<string>();
        var policy = Substitute.For<IOneTimeLinkPolicy>();
        policy.GetAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("policy");
            return ValueTask.FromResult(new OneTimeLinkSettings(true, TimeSpan.FromDays(7)));
        });
        _links.GetAsync(_link.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("read link");
            return Task.FromResult<Versioned<OneTimeLink>?>(new Versioned<OneTimeLink>(_link, "link-etag"));
        });
        _users.GetAsync(_creatorId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("read creator");
            return Task.FromResult<Versioned<User>?>(OneTimeLinkSamples.UserRow(_creatorId, UserStatus.Active));
        });
        _links.TryConsumeAsync(_link.Id, "link-etag", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("consume");
            return Task.FromResult(true);
        });
        _auditLog.FailWhen = _ =>
        {
            calls.Add("audit");
            return null;
        };
        _links.GetIndexEntryAsync(_creatorId, _link.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("read index");
            return Task.FromResult<OneTimeLinkIndexEntry?>(OneTimeLinkSamples.IndexFor(_link, [1]));
        });
        _links.AddNoticeAsync(Arg.Any<OneTimeLinkNotice>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("notice");
            return Task.CompletedTask;
        });
        _links.RemoveBookkeepingAsync(_creatorId, _link.Id, _link.ExpiresAt, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            calls.Add("bookkeeping");
            return Task.CompletedTask;
        });

        await Handler(policy).Handle(Command(), Token);

        string[] expected = ["policy", "read link", "read creator", "consume", "audit", "read index", "notice", "bookkeeping"];
        Assert.Equal(expected, calls);
    }

    [Fact]
    public async Task Handle_Disabled_ReturnsNotFoundAndReadsNothing()
    {
        var result = await Handler(OneTimeLinkSamples.Policy(enabled: false)).Handle(Command(), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_links.ReceivedCalls());
        Assert.Empty(_auditLog.Entries);
    }

    [Fact]
    public async Task Handle_UnknownLink_ReturnsNotFound()
    {
        _links.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<OneTimeLink>?>(null));

        var result = await Handler().Handle(Command(Guid.NewGuid()), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_auditLog.Entries);
        await _links.DidNotReceive().TryConsumeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task Handle_ExpiryAroundNow_OnlyALinkStillValidOpens(long ticksFromNow, bool opens)
    {
        var expiring = OneTimeLinkSamples.NewLink(_creatorId, Guid.NewGuid(), Guid.NewGuid(), OneTimeLinkSamples.Now + TimeSpan.FromTicks(ticksFromNow));
        GivenLinkStored(expiring);

        var result = await Handler().Handle(Command(expiring.Id), Token);

        Assert.Equal(opens, result.IsSuccess);
        Assert.Equal(opens ? 1 : 0, _auditLog.Entries.Count);
    }

    [Fact]
    public async Task Handle_CreatorMissing_ReturnsNotFound()
    {
        _users.GetAsync(_creatorId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<User>?>(null));

        var result = await Handler().Handle(Command(), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_auditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_CreatorLocked_ReturnsNotFoundWithoutConsuming()
    {
        GivenCreator(UserStatus.Locked);

        var result = await Handler().Handle(Command(), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await _links.DidNotReceive().TryConsumeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Empty(_auditLog.Entries);
    }

    [Fact]
    public async Task Handle_LostTheConsume_WritesNoAuditAndNoNotice()
    {
        _links.TryConsumeAsync(_link.Id, "link-etag", Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));

        var result = await Handler().Handle(Command(), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_auditLog.Entries);
        await _links.DidNotReceive().AddNoticeAsync(Arg.Any<OneTimeLinkNotice>(), Arg.Any<CancellationToken>());
        await _links.DidNotReceive().GetIndexEntryAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OpenedTwice_SecondReturnsNotFound()
    {
        var handler = Handler();
        var first = await handler.Handle(Command(), Token);
        _links.GetAsync(_link.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Versioned<OneTimeLink>?>(null));

        var second = await handler.Handle(Command(), Token);

        Assert.True(first.IsSuccess);
        Assert.Equal(ResultFailureKind.NotFound, second.FailureKind);
        Assert.Single(_auditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AuditFails_ThrowsAndReturnsNoPayload()
    {
        _auditLog.Failure = new InvalidOperationException("audit down");
        Result<OneTimeLinkPayloadDto>? result = null;

        await Assert.ThrowsAsync<InvalidOperationException>(async () => result = await Handler().Handle(Command(), Token));

        Assert.Null(result);
        await _links.DidNotReceive().AddNoticeAsync(Arg.Any<OneTimeLinkNotice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClientAbortsAfterTheDelete_StillWritesTheAudit()
    {
        using var requestCancellation = new CancellationTokenSource();
        _links.TryConsumeAsync(_link.Id, "link-etag", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            requestCancellation.Cancel();
            return Task.FromResult(true);
        });

        var result = await Handler().Handle(Command(), requestCancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuditEventType.OneTimeLinkOpened, Assert.Single(_auditLog.Entries).EventType);
    }

    [Fact]
    public async Task Handle_NoticeFails_StillReturnsThePayloadAndLogsAnError()
    {
        _links.AddNoticeAsync(Arg.Any<OneTimeLinkNotice>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("notice store down")));

        var result = await Handler().Handle(Command(), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(Convert.ToBase64String(_link.Payload), result.Value!.Payload);
        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Contains("add notice", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), error.Message, StringComparison.Ordinal);
        await _links.Received(1).RemoveBookkeepingAsync(_creatorId, _link.Id, _link.ExpiresAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BookkeepingFails_StillReturnsThePayload()
    {
        _links.RemoveBookkeepingAsync(_creatorId, _link.Id, _link.ExpiresAt, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("bookkeeping down")));

        var result = await Handler().Handle(Command(), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(Convert.ToBase64String(_link.Payload), result.Value!.Payload);
        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Contains("remove bookkeeping", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handle_IndexReadFails_ReturnsThePayloadWithoutANoticeAndStillClearsBookkeeping()
    {
        _links.GetIndexEntryAsync(_creatorId, _link.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<OneTimeLinkIndexEntry?>(new InvalidOperationException("index down")));

        var result = await Handler().Handle(Command(), Token);

        Assert.True(result.IsSuccess);
        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Contains("read index", error.Message, StringComparison.Ordinal);
        await _links.DidNotReceive().AddNoticeAsync(Arg.Any<OneTimeLinkNotice>(), Arg.Any<CancellationToken>());
        await _links.Received(1).RemoveBookkeepingAsync(_creatorId, _link.Id, _link.ExpiresAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoIndexRow_ReturnsThePayloadWithoutANoticeAndLogsAnError()
    {
        _links.GetIndexEntryAsync(_creatorId, _link.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<OneTimeLinkIndexEntry?>(null));

        var result = await Handler().Handle(Command(), Token);

        Assert.True(result.IsSuccess);
        var error = Assert.Single(_logger.Records, record => record.Level == LogLevel.Error);
        Assert.Contains("no index row", error.Message, StringComparison.Ordinal);
        await _links.DidNotReceive().AddNoticeAsync(Arg.Any<OneTimeLinkNotice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserAgentWithControlCharactersAnd4000Characters_StoresItCleanedAndTruncated()
    {
        var agent = "agent\u0001\u0007" + new string('x', 4000);

        await Handler().Handle(Command(agent: agent), Token);

        var entry = Assert.Single(_auditLog.Entries);
        Assert.True(entry.UserAgent!.Length <= OneTimeLinkLimits.UserAgentMaxLength);
        Assert.DoesNotContain(entry.UserAgent!, character => char.IsControl(character));
    }

    [Fact]
    public void Command_ToString_ReturnsTypeNameOnly()
    {
        var command = new OpenOneTimeLinkCommand(Guid.NewGuid(), ClientIp, UserAgent);

        Assert.Equal("OpenOneTimeLinkCommand", command.ToString());
    }
}
