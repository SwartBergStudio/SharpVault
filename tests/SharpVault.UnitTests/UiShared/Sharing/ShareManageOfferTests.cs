// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.Dtos.Directory;
using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

[Trait("Category", "Security")]
public sealed class ShareManageOfferTests : SharingDialogTestHost
{
    private const string ManageValue = "Manage";
    private const string GrantSelectPrefix = "share-grant-permission-";
    private const string DialogSelectId = "share-dialog-permission";

    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _adaId = Guid.NewGuid();

    private void ReturnsGrants(bool canGrantManage) =>
        GrantsClient.ListSecretGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<ShareGrantListDto>.Success(new ShareGrantListDto
            {
                Items = [Grant(_adaId, "Ada Lovelace")],
                CanGrantManage = canGrantManage,
            })));

    private IRenderedComponent<SharePanel> RenderPanel() =>
        Render<SharePanel>(parameters => parameters
            .Add(panel => panel.OwnerId, Owner.UserId)
            .Add(panel => panel.Kind, SharedItemKindDto.Secret)
            .Add(panel => panel.ItemId, _itemId));

    private static List<string> OptionValues(IRenderedComponent<SharePanel> cut, string selectId) =>
        cut.FindComponents<SvSelect>().Single(select => select.Instance.Id == selectId).Instance.Options.Select(option => option.Value).ToList();

    [Fact]
    public void SharePanel_Manager_DoesNotOfferManage()
    {
        ReturnsGrants(canGrantManage: false);
        var cut = RenderPanel();

        cut.WaitForElement($"#{GrantSelectPrefix}{_adaId:D}");

        Assert.DoesNotContain(ManageValue, OptionValues(cut, $"{GrantSelectPrefix}{_adaId:D}"));
    }

    [Fact]
    public void SharePanel_Owner_OffersManage()
    {
        ReturnsGrants(canGrantManage: true);
        var cut = RenderPanel();

        cut.WaitForElement($"#{GrantSelectPrefix}{_adaId:D}");

        Assert.Contains(ManageValue, OptionValues(cut, $"{GrantSelectPrefix}{_adaId:D}"));
    }

    [Fact]
    public void ShareDialog_CanGrantManageFalse_DoesNotOfferManage()
    {
        var cut = Render<ShareDialog>(parameters => parameters
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.CanGrantManage, false)
            .Add(dialog => dialog.Search, (_, _) => Task.FromResult(ApiResponse<DirectoryUserListDto>.Success(new DirectoryUserListDto())))
            .Add(dialog => dialog.OnConfirm, _ => { })
            .Add(dialog => dialog.OnCancel, () => { }));

        var values = Provider.FindComponents<SvSelect>().Single(select => select.Instance.Id == DialogSelectId).Instance.Options.Select(option => option.Value).ToList();

        Assert.NotEmpty(values);
        Assert.DoesNotContain(ManageValue, values);
        Assert.NotNull(cut);
    }
}
