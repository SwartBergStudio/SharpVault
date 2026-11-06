// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Components.Transfers;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Transfers;

public sealed class TransferSharesReviewTests : TransferUiTestHost
{
    private readonly Guid _itemId = Guid.NewGuid();

    private readonly Guid _recipientId = Guid.NewGuid();

    private static OwnedShareDto Share(Guid itemId, Guid recipientId, string? name = "Build server", bool damaged = false) => new()
    {
        Kind = SharedItemKindDto.Secret,
        ItemId = itemId,
        Name = name,
        IsDamaged = damaged,
        RecipientId = recipientId,
        RecipientDisplayName = "User Two",
        Permission = SharePermissionDto.View,
        GrantedAt = At,
    };

    private IRenderedComponent<TransferSharesReview> RenderWith(params OwnedShareDto[] shares)
    {
        Grants.ListOwnedSharesAsync(TransferSharesReview.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OwnedSharePageDto>.Success(new OwnedSharePageDto { Items = [.. shares] })));
        return Render<TransferSharesReview>();
    }

    [Fact]
    public void Render_ListsTheItemNameAndTheRecipient()
    {
        var cut = RenderWith(Share(_itemId, _recipientId));

        var table = cut.Find("#transfer-shares-table").TextContent;
        Assert.Contains("Build server", table, StringComparison.Ordinal);
        Assert.Contains("User Two", table, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_ShowsTheHint()
    {
        var cut = RenderWith();

        Assert.Contains("You have not shared anything", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#transfer-shares-table"));
    }

    [Fact]
    public void DamagedItem_ShowsTheUnreadablePlaceholderAndNoName()
    {
        var cut = RenderWith(Share(_itemId, _recipientId, name: null, damaged: true));

        var table = cut.Find("#transfer-shares-table").TextContent;
        Assert.Contains("Unreadable item", table, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_ConfirmsThenCallsTheGrantClientForTheCaller()
    {
        var provider = RenderDialogProviders();
        Grants.RevokeSecretGrantAsync(CallerId, _itemId, _recipientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = RenderWith(Share(_itemId, _recipientId));

        await cut.Find($"#transfer-share-revoke-{Id(_itemId)}-{Id(_recipientId)}").ClickAsync(new MouseEventArgs());
        await provider.Find("#transfer-share-revoke-dialog-confirm").ClickAsync(new MouseEventArgs());

        _ = Grants.Received(1).RevokeSecretGrantAsync(CallerId, _itemId, _recipientId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeNotFound_SaysTheShareWasAlreadyRemoved()
    {
        var provider = RenderDialogProviders();
        Grants.RevokeSecretGrantAsync(CallerId, _itemId, _recipientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.NotFound()));
        var cut = RenderWith(Share(_itemId, _recipientId));

        await cut.Find($"#transfer-share-revoke-{Id(_itemId)}-{Id(_recipientId)}").ClickAsync(new MouseEventArgs());
        await provider.Find("#transfer-share-revoke-dialog-confirm").ClickAsync(new MouseEventArgs());

        Assert.Contains(TransferSharesReview.GoneText, cut.Find("#transfer-shares-status").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeFails_KeepsTheRowAndShowsTheStatus()
    {
        var provider = RenderDialogProviders();
        Grants.RevokeSecretGrantAsync(CallerId, _itemId, _recipientId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Failed()));
        var cut = RenderWith(Share(_itemId, _recipientId));

        await cut.Find($"#transfer-share-revoke-{Id(_itemId)}-{Id(_recipientId)}").ClickAsync(new MouseEventArgs());
        await provider.Find("#transfer-share-revoke-dialog-confirm").ClickAsync(new MouseEventArgs());

        Assert.NotNull(cut.Find($"#transfer-share-revoke-{Id(_itemId)}-{Id(_recipientId)}"));
        Assert.NotEmpty(cut.Find("#transfer-shares-status").TextContent.Trim());
    }

    [Fact]
    public void Route_TransferShares_RendersTheReviewPage()
    {
        Grants.ListOwnedSharesAsync(TransferSharesReview.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OwnedSharePageDto>.Success(new OwnedSharePageDto { Items = [Share(_itemId, _recipientId)] })));

        var cut = RenderRouter(TransferRoutes.Shares);

        cut.WaitForElement("#transfer-shares-table");
        Assert.Empty(cut.FindAll("#route-not-found"));
    }
}
