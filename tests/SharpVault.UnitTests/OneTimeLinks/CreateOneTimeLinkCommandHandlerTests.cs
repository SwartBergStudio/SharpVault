// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Commands;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Domain.Sharing;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Application.Vault;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class CreateOneTimeLinkCommandHandlerTests : VaultHandlerTestBase
{
    private readonly IOneTimeLinkStore _links = Substitute.For<IOneTimeLinkStore>();

    public CreateOneTimeLinkCommandHandlerTests()
    {
        _links.ListIndexAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OneTimeLinkSamples.Page()));
        _links.CreateAsync(Arg.Any<OneTimeLink>(), Arg.Any<OneTimeLinkIndexEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(StoreWriteResult.Written));
        GivenNoShares();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private CreateOneTimeLinkCommandHandler Handler(ICurrentUser? requester = null, bool enabled = true, TimeSpan? maxLifetime = null)
    {
        var who = requester ?? Current;
        return new(
            OneTimeLinkSamples.Policy(enabled, maxLifetime),
            new ShareManagementGate(Store, new ShareAccessResolver(Store, who), who),
            Store,
            _links,
            OneTimeLinkSamples.AuditWriter(AuditLog, Time),
            who,
            new StubRequestMetadata(),
            Time);
    }

    private void GivenNoShares()
    {
        Store.GetShareGrantAsync(Arg.Any<Guid>(), Arg.Any<ShareItemKind>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Versioned<ShareGrant>?>(null));
        Store.ListFolderGrantsToRecipientAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ShareGrant>>([]));
    }

    private void GivenSecretThatIsGoneAfterTheGate(Versioned<Secret> secret) =>
        Store.GetSecretAsync(Owner, secret.Value.Id, Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<Versioned<Secret>?>(secret),
            Task.FromResult<Versioned<Secret>?>(null));

    [Fact]
    public async Task Handle_Success_StoresTheLinkWithTheRequestedLifetimeAndAuditsCreated()
    {
        var secret = GivenSecret(SecretWith("Name"));
        var payload = OneTimeLinkSamples.PayloadBase64();

        var result = await Handler().Handle(new CreateOneTimeLinkCommand(Owner, secret.Value.Id, payload, 30), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(secret.Value.Id, result.Value!.ItemId);
        Assert.Equal(Now.AddMinutes(30), result.Value.ExpiresAt);
        Assert.Null(result.Value.ItemName);
        await _links.Received(1).CreateAsync(
            Arg.Is<OneTimeLink>(link => link.Id == result.Value.Id && link.CreatorId == Owner && link.OwnerId == Owner && Convert.ToBase64String(link.Payload) == payload),
            Arg.Is<OneTimeLinkIndexEntry>(index => index.LinkId == result.Value.Id && index.NameEnvelope.SequenceEqual(secret.Value.NameEnvelope)),
            Arg.Any<CancellationToken>());
        var entry = Assert.Single(AuditLog.Entries);
        Assert.Equal(AuditEventType.OneTimeLinkCreated, entry.EventType);
        Assert.Equal(result.Value.Id.ToString("D"), entry.TargetId);
        Assert.Equal(Owner, entry.ActorId);
    }

    [Fact]
    public async Task Handle_Disabled_ReturnsForbiddenAndStoresNothing()
    {
        GivenSecret(SecretWith("Name"));

        var result = await Handler(enabled: false).Handle(new CreateOneTimeLinkCommand(Owner, Guid.NewGuid(), OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.Equal(ResultFailureKind.Forbidden, result.FailureKind);
        Assert.Empty(Store.ReceivedCalls());
        Assert.Empty(_links.ReceivedCalls());
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_SecretMissing_ReturnsNotFoundAndStoresNothing()
    {
        var result = await Handler().Handle(new CreateOneTimeLinkCommand(Owner, Guid.NewGuid(), OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_links.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_SecretGoneAfterTheGate_ReturnsNotFoundAndStoresNothing()
    {
        var secret = SecretWith("Name");
        GivenSecretThatIsGoneAfterTheGate(new Versioned<Secret>(secret, NextETag()));

        var result = await Handler().Handle(new CreateOneTimeLinkCommand(Owner, secret.Id, OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_links.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_RecycledSecret_ReturnsNotFoundAndNeverLooksAtRecycledRows()
    {
        var secret = GivenRecycledSecret(SecretWith("Recycled"));

        var result = await Handler().Handle(new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_links.ReceivedCalls());
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_AnotherOwnersIdWithMyOwnSecretId_ReturnsNotFoundAndStoresNothing()
    {
        var secret = GivenSecret(SecretWith("Mine"));
        var otherOwner = Guid.NewGuid();

        var result = await Handler().Handle(new CreateOneTimeLinkCommand(otherOwner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_links.ReceivedCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_ManageRevokedAfterSnapshot_ReturnsNotFoundAndStoresNothing()
    {
        var secret = GivenSecret(SecretWith("Shared"));
        var formerRecipient = new StubCurrentUser();

        var result = await Handler(formerRecipient).Handle(new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        Assert.Empty(_links.ReceivedCalls());
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public async Task Handle_LifetimeOverTheConfiguredMaximum_IsRejectedAndStoresNothing()
    {
        var secret = GivenSecret(SecretWith("Name"));

        var result = await Handler(maxLifetime: TimeSpan.FromDays(1)).Handle(
            new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), 1441),
            Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(OneTimeLinkMessages.LifetimeTooLong, result.Error);
        Assert.Empty(_links.ReceivedCalls());
    }

    [Fact]
    public async Task Handle_LifetimeAtTheConfiguredMaximum_IsAccepted()
    {
        var secret = GivenSecret(SecretWith("Name"));

        var result = await Handler(maxLifetime: TimeSpan.FromDays(1)).Handle(
            new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), 1440),
            Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddDays(1), result.Value!.ExpiresAt);
    }

    [Fact]
    public async Task Handle_NoLifetime_UsesTwentyFourHoursWhenTheMaximumAllowsIt()
    {
        var secret = GivenSecret(SecretWith("Name"));

        var result = await Handler(maxLifetime: TimeSpan.FromDays(7)).Handle(
            new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null),
            Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddHours(24), result.Value!.ExpiresAt);
    }

    [Fact]
    public async Task Handle_NoLifetimeAndShortMaximum_ClampsToTheMaximum()
    {
        var secret = GivenSecret(SecretWith("Name"));

        var result = await Handler(maxLifetime: TimeSpan.FromHours(2)).Handle(
            new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null),
            Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddHours(2), result.Value!.ExpiresAt);
    }

    [Fact]
    public async Task Handle_ActiveLinksAtTheLimit_IsRejectedAndStoresNothing()
    {
        var secret = GivenSecret(SecretWith("Name"));
        var atLimit = Enumerable.Range(0, OneTimeLinkLimits.MaxActiveLinksPerCreator).Select(_ => new OneTimeLinkIndexEntry()).ToArray();
        _links.ListIndexAsync(Owner, Now, OneTimeLinkLimits.MaxActiveLinksPerCreator, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OneTimeLinkSamples.Page(atLimit)));

        var result = await Handler().Handle(new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(OneTimeLinkMessages.TooManyActiveLinks, result.Error);
        await _links.DidNotReceive().CreateAsync(Arg.Any<OneTimeLink>(), Arg.Any<OneTimeLinkIndexEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveLinksJustBelowTheLimit_IsAccepted()
    {
        var secret = GivenSecret(SecretWith("Name"));
        var belowLimit = Enumerable.Range(0, OneTimeLinkLimits.MaxActiveLinksPerCreator - 1).Select(_ => new OneTimeLinkIndexEntry()).ToArray();
        _links.ListIndexAsync(Owner, Now, OneTimeLinkLimits.MaxActiveLinksPerCreator, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(OneTimeLinkSamples.Page(belowLimit)));

        var result = await Handler().Handle(new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_CountsActiveLinksAtNowWithTheLimitAsThePageSize()
    {
        var secret = GivenSecret(SecretWith("Name"));

        await Handler().Handle(new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null), Token);

        await _links.Received(1).ListIndexAsync(Owner, Now, OneTimeLinkLimits.MaxActiveLinksPerCreator, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyExists_ReturnsConflictAndAuditsNothing()
    {
        var secret = GivenSecret(SecretWith("Name"));
        _links.CreateAsync(Arg.Any<OneTimeLink>(), Arg.Any<OneTimeLinkIndexEntry>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(StoreWriteResult.AlreadyExists));

        var result = await Handler().Handle(new CreateOneTimeLinkCommand(Owner, secret.Value.Id, OneTimeLinkSamples.PayloadBase64(), null), Token);

        Assert.Equal(ResultFailureKind.Conflict, result.FailureKind);
        Assert.Empty(AuditLog.Entries);
    }

    [Fact]
    public void Command_ToString_ReturnsTypeNameOnly()
    {
        var command = new CreateOneTimeLinkCommand(Guid.NewGuid(), Guid.NewGuid(), "AAAA", 30);

        Assert.Equal("CreateOneTimeLinkCommand", command.ToString());
    }
}
