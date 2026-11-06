// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UnitTests.UiShared.Transfers;

public sealed class TransferRequestFormTests : TransferUiTestHost
{
    private const string ValidReason = "Role change, the vault moves to the new owner";

    private readonly Guid _sourceId = Guid.NewGuid();

    private readonly Guid _targetId = Guid.NewGuid();

    private readonly Guid _folderId = Guid.NewGuid();

    public TransferRequestFormTests()
    {
        UsersClient.ListUsersAsync(Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<UserPageDto>.Success(new UserPageDto
            {
                Users =
                [
                    User(CallerId, "Admin One"),
                    User(_sourceId, "User One"),
                    User(_targetId, "User Two"),
                ],
            })));
    }

    private IRenderedComponent<Router> OpenForm(string path = "")
    {
        var query = path.Length == 0 ? $"?sourceUserId={Id(_sourceId)}" : path;
        return RenderRouter(TransferRoutes.NewTransfer + query);
    }

    private void ReturnsPreview(TransferPreviewDto preview) =>
        Transfers.PreviewAsync(_sourceId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferPreviewDto>.Success(preview)));

    private static async Task ChooseAsync(IRenderedComponent<Router> cut, string selectId, string value)
    {
        var select = cut.FindComponents<SvSelect>().Single(component => component.Instance.Id == selectId);
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(value));
        cut.Render();
    }

    [Fact]
    public async Task QueryParameter_PreselectsTheSource_AndLoadsItsPreview()
    {
        ReturnsPreview(Preview());

        var cut = OpenForm();

        cut.WaitForElement("#transfer-preview-items");
        Assert.Contains("4", cut.Find("#transfer-preview-items").TextContent, StringComparison.Ordinal);
        _ = Transfers.Received().PreviewAsync(_sourceId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RoleChange_SelectsTheFolderScope_AndShowsOnlyTheOpaqueReference()
    {
        ReturnsPreview(Preview(folders: Folder(_folderId, "1A2B3C4D")));

        var cut = OpenForm();
        cut.WaitForElement("#transfer-preview-items");
        await ChooseAsync(cut, "transfer-category", ReasonCategoryDto.RoleChange.ToString());

        cut.WaitForElement($"#transfer-folder-ref-{Id(_folderId)}");
        Assert.Equal("Folder 1A2B3C4D, created 2026-03-01, 3 items", cut.Find($"#transfer-folder-ref-{Id(_folderId)}").TextContent.Trim());
    }

    [Fact]
    public async Task FolderPreview_NeverCarriesAFolderNameInTheMarkup()
    {
        ReturnsPreview(Preview(folders: Folder(_folderId, "1A2B3C4D")));

        var cut = OpenForm();
        cut.WaitForElement("#transfer-preview-items");
        await ChooseAsync(cut, "transfer-category", ReasonCategoryDto.RoleChange.ToString());

        cut.WaitForElement($"#transfer-folder-ref-{Id(_folderId)}");
        Assert.DoesNotContain("Finance", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Secrets", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Include folder 1A2B3C4D", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneAdminOnly_ShowsTheSecondAdminNoteAndDisablesSubmit()
    {
        ReturnsPreview(Preview(secondAdminAvailable: false));

        var cut = OpenForm();

        cut.WaitForElement("#transfer-form-second-admin");
        Assert.True(cut.Find("#transfer-submit").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ShortReason_ShowsTheErrorAndCreatesNothing()
    {
        ReturnsPreview(Preview());
        var cut = OpenForm();
        cut.WaitForElement("#transfer-preview-items");

        cut.Find("#transfer-reason").Input("short");
        await cut.Find("#transfer-submit").ClickAsync(new MouseEventArgs());

        _ = Transfers.DidNotReceiveWithAnyArgs().CreateAsync(default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ValidRequest_CreatesOnceAndNavigatesToTheDetail()
    {
        ReturnsPreview(Preview());
        var transferId = Guid.NewGuid();
        Transfers.CreateAsync(Arg.Any<CreateTransferRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferDto>.Success(Transfer(transferId, CallerId))));
        var cut = OpenForm();
        cut.WaitForElement("#transfer-preview-items");
        await ChooseAsync(cut, "transfer-target", Id(_targetId));
        cut.Find("#transfer-reason").Input(ValidReason);

        await cut.Find("#transfer-submit").ClickAsync(new MouseEventArgs());

        _ = Transfers.Received(1).CreateAsync(
            Arg.Is<CreateTransferRequestDto>(request => request.SourceUserId == _sourceId && request.Reason == ValidReason),
            Arg.Any<CancellationToken>());
        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith($"/admin/transfers/{Id(transferId)}", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task NonAdmin_ShowsTheAdministratorsOnlyPanelAndLoadsNoPreview()
    {
        SignInAsNonAdmin();

        var cut = OpenForm();

        cut.WaitForElement("#transfer-form-forbidden");
        _ = Transfers.DidNotReceiveWithAnyArgs().PreviewAsync(default, Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(cut.FindAll("#transfer-submit"));
    }
}
