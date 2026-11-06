// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Sharing.Queries;
using SharpVault.UnitTests.Application.Sharing.SharedItems;

namespace SharpVault.UnitTests.Application.Sharing.Queries;

[Trait("Category", "Security")]
public sealed class ListOwnedSharesHandlerTests : SharedItemsHandlerTestBase
{
    private ListOwnedSharesQueryHandler Handler() => new(Store, SharedRings, Current);

    [Fact]
    public async Task Handle_StorageRejectsTheContinuationToken_ReturnsRejectedInsteadOfThrowing()
    {
        var token = ContinuationTokens.ForStorage("stale");
        Store.ListShareGrantsByOwnerAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ShareGrantPage>>(_ => throw new InvalidContinuationTokenException());

        var result = await Handler().Handle(new ListOwnedSharesQuery(10, token), Token);

        Assert.Equal(ResultFailureKind.Rejected, result.FailureKind);
        Assert.Equal(SharingMessages.InvalidContinuationToken, result.Error);
    }
}
