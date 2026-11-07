// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.OneTimeLinks.Queries;
using SharpVault.Domain.OneTimeLinks;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Application.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class ListOneTimeLinkNoticesQueryHandlerTests : VaultHandlerTestBase
{
    private readonly IOneTimeLinkStore _links = Substitute.For<IOneTimeLinkStore>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ListOneTimeLinkNoticesQueryHandler Handler() => new(_links, KeyRings, Current);

    private OneTimeLinkNotice NoticeFor(Secret secret, byte[]? nameEnvelope = null) => new()
    {
        NoticeId = "n_0000000000000000001_" + Guid.NewGuid().ToString("N"),
        LinkId = Guid.NewGuid(),
        CreatorId = Owner,
        OwnerId = Owner,
        SecretId = secret.Id,
        NameEnvelope = nameEnvelope ?? secret.NameEnvelope,
        MetadataKeyVersion = secret.MetadataKeyVersion,
        OpenedAt = Now,
    };

    private void GivenNotices(params OneTimeLinkNotice[] notices) =>
        _links.ListNoticesAsync(Owner, OneTimeLinkLimits.NoticeListLimit, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OneTimeLinkNotice>>(notices));

    [Fact]
    public async Task Handle_NoNotices_OpensNoKeyRing()
    {
        GivenNotices();

        var result = await Handler().Handle(new ListOneTimeLinkNoticesQuery(), Token);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, Encryptor.OpenCount);
        await Store.DidNotReceive().GetKeyRingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ListsAtMostTheNoticeLimit()
    {
        GivenNotices();

        await Handler().Handle(new ListOneTimeLinkNoticesQuery(), Token);

        await _links.Received(1).ListNoticesAsync(Owner, OneTimeLinkLimits.NoticeListLimit, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Notices_ReturnTheDecryptedNamesAndTheNoticeFields()
    {
        var secret = SecretWith("Vault item");
        var notice = NoticeFor(secret);
        GivenNotices(notice);

        var result = await Handler().Handle(new ListOneTimeLinkNoticesQuery(), Token);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(notice.NoticeId, item.NoticeId);
        Assert.Equal(notice.LinkId, item.LinkId);
        Assert.Equal(notice.OpenedAt, item.OpenedAt);
        Assert.Equal("Vault item", item.ItemName);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_UnreadableOwnerRing_ReturnsNullNames()
    {
        GivenNotices(NoticeFor(SecretWith("Hidden")));
        GivenUnreadableKeyRing();

        var result = await Handler().Handle(new ListOneTimeLinkNoticesQuery(), Token);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(result.Value!.Items).ItemName);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DamagedName_NullsOnlyThatRow()
    {
        var good = SecretWith("Good");
        GivenNotices(NoticeFor(DamagedSecret()), NoticeFor(good));

        var result = await Handler().Handle(new ListOneTimeLinkNoticesQuery(), Token);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Items[0].ItemName);
        Assert.Equal("Good", result.Value.Items[1].ItemName);
    }
}
