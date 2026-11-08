// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.OneTimeLinks.Commands;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class DismissOneTimeLinkNoticeCommandHandlerTests
{
    private const string NoticeId = "n_0000000000000000001_0123456789abcdef0123456789abcdef";

    private readonly IOneTimeLinkStore _links = Substitute.For<IOneTimeLinkStore>();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_Written_ReturnsDone()
    {
        var caller = new StubCurrentUser();
        _links.DismissNoticeAsync(caller.UserId, NoticeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(StoreWriteResult.Written));

        var result = await new DismissOneTimeLinkNoticeCommandHandler(_links, caller).Handle(new DismissOneTimeLinkNoticeCommand(NoticeId), Token);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    [InlineData(StoreWriteResult.AlreadyExists, ResultFailureKind.NotFound)]
    public async Task Handle_StoreFailure_MapsToTheMatchingResult(StoreWriteResult outcome, ResultFailureKind expectedFailure)
    {
        var caller = new StubCurrentUser();
        _links.DismissNoticeAsync(caller.UserId, NoticeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(outcome));

        var result = await new DismissOneTimeLinkNoticeCommandHandler(_links, caller).Handle(new DismissOneTimeLinkNoticeCommand(NoticeId), Token);

        Assert.Equal(expectedFailure, result.FailureKind);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handle_DismissesOnlyInTheCallersPartition()
    {
        var caller = new StubCurrentUser();
        _links.DismissNoticeAsync(caller.UserId, NoticeId, Arg.Any<CancellationToken>()).Returns(Task.FromResult(StoreWriteResult.NotFound));

        var result = await new DismissOneTimeLinkNoticeCommandHandler(_links, caller).Handle(new DismissOneTimeLinkNoticeCommand(NoticeId), Token);

        Assert.Equal(ResultFailureKind.NotFound, result.FailureKind);
        await _links.Received(1).DismissNoticeAsync(caller.UserId, NoticeId, Arg.Any<CancellationToken>());
    }
}
