// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.OneTimeLinks.Queries;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Application.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class ListOneTimeLinksQueryHandlerTests : VaultHandlerTestBase
{
    private readonly IOneTimeLinkStore _links = Substitute.For<IOneTimeLinkStore>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ListOneTimeLinksQueryHandler Handler() => new(_links, KeyRings, Current, Time);

    private OneTimeLinkIndexEntry EntryFor(Secret secret, byte[]? nameEnvelope = null) => new()
    {
        LinkId = Guid.NewGuid(),
        CreatorId = Owner,
        OwnerId = Owner,
        SecretId = secret.Id,
        NameEnvelope = nameEnvelope ?? secret.NameEnvelope,
        MetadataKeyVersion = secret.MetadataKeyVersion,
        CreatedAt = Now,
        ExpiresAt = Now.AddDays(1),
    };

    private void GivenPage(OneTimeLinkIndexPage page) =>
        _links.ListIndexAsync(Owner, Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(page));

    [Fact]
    public async Task Handle_Rows_ReturnTheDecryptedNamesAndTheirIds()
    {
        var alpha = SecretWith("Alpha");
        var beta = SecretWith("Beta");
        var alphaEntry = EntryFor(alpha);
        var betaEntry = EntryFor(beta);
        GivenPage(OneTimeLinkSamples.Page(alphaEntry, betaEntry));

        var result = await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(alphaEntry.LinkId, result.Value.Items[0].Id);
        Assert.Equal("Alpha", result.Value.Items[0].ItemName);
        Assert.Equal(beta.Id, result.Value.Items[1].ItemId);
        Assert.Equal("Beta", result.Value.Items[1].ItemName);
        Assert.Null(result.Value.ContinuationToken);
    }

    [Fact]
    public async Task Handle_ListsActiveLinksAtNowWithTheRequestedPageSize()
    {
        GivenPage(OneTimeLinkSamples.Page());

        await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        await _links.Received(1).ListIndexAsync(Owner, Now, 25, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NextPage_ContinuationTokenIsTheNextLinkIdInNFormat()
    {
        var next = Guid.NewGuid();
        _links.ListIndexAsync(Owner, Arg.Any<DateTimeOffset?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OneTimeLinkIndexPage([], next)));

        var result = await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        Assert.Equal(next.ToString("N"), result.Value!.ContinuationToken);
    }

    [Fact]
    public async Task Handle_ContinuationToken_ResumesAfterThatLinkId()
    {
        var after = Guid.NewGuid();
        GivenPage(OneTimeLinkSamples.Page());

        await Handler().Handle(new ListOneTimeLinksQuery(25, after.ToString("N")), Token);

        await _links.Received(1).ListIndexAsync(Owner, Now, 25, after, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_UnreadableOwnerRing_ReturnsRowsWithNullNames()
    {
        GivenPage(OneTimeLinkSamples.Page(EntryFor(SecretWith("Hidden"))));
        GivenUnreadableKeyRing();

        var result = await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(result.Value!.Items).ItemName);
    }

    [Fact]
    public async Task Handle_NoKeyRing_ReturnsRowsWithNullNames()
    {
        GivenPage(OneTimeLinkSamples.Page(EntryFor(SecretWith("Missing"))));
        GivenNoKeyRing();

        var result = await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(result.Value!.Items).ItemName);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DamagedNameEnvelope_NullsOnlyThatRow()
    {
        var good = SecretWith("Good");
        GivenPage(OneTimeLinkSamples.Page(EntryFor(DamagedSecret()), EntryFor(good)));

        var result = await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Items[0].ItemName);
        Assert.Equal("Good", result.Value.Items[1].ItemName);
    }

    [Fact]
    public async Task Handle_TwoRowsOfOneOwner_OpensTheKeyRingOnce()
    {
        GivenPage(OneTimeLinkSamples.Page(EntryFor(SecretWith("One")), EntryFor(SecretWith("Two"))));

        await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        Assert.Equal(1, Encryptor.OpenCount);
    }

    [Fact]
    public async Task Handle_NoRows_ReturnsAnEmptyPageWithoutOpeningAnyRing()
    {
        GivenPage(OneTimeLinkSamples.Page());

        var result = await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, Encryptor.OpenCount);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_SecretRenamedAfterCreate_ShowsTheSnapshotName()
    {
        var secret = SecretWith("Original");
        var snapshotEnvelope = secret.NameEnvelope;
        SecretMetadataCipher.Encrypt(MetadataSession, secret, new SecretMetadata("Renamed", null, null, []));
        GivenPage(OneTimeLinkSamples.Page(EntryFor(secret, snapshotEnvelope)));

        var result = await Handler().Handle(new ListOneTimeLinksQuery(25, null), Token);

        Assert.Equal("Original", Assert.Single(result.Value!.Items).ItemName);
    }
}
