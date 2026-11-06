// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedSecretEditorGateTests : SharedDetailTestHost
{
    private const string NotesText = "gate-note-body-52";

    public SharedSecretEditorGateTests()
    {
        ReturnsReveal(NotesText);
        SharedItemsClient.UpdateSecretAsync(OwnerId, SecretId, Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretDto>.Success(new SecretDto { Id = SecretId })));
    }

    private IRenderedComponent<SharedSecretEditPage> RenderPage(Guid? ownerId = null, Guid? secretId = null)
    {
        var cut = Render<SharedSecretEditPage>(parameters => parameters
            .Add(page => page.OwnerId, ownerId ?? OwnerId)
            .Add(page => page.SecretId, secretId ?? SecretId));
        cut.WaitForElement("#shared-secret-editor-heading");
        return cut;
    }

    private void UpdateReturns(ApiResponse<SecretDto> response) =>
        SharedItemsClient.UpdateSecretAsync(OwnerId, SecretId, Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(response));

    private static UpdateSecretRequestDto ModelOf(IRenderedComponent<SharedSecretEditPage> cut) =>
        (UpdateSecretRequestDto)typeof(SharedSecretEditor).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.FindComponent<SharedSecretEditor>().Instance)!;

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(SharePermissionDto.View)]
    [InlineData((SharePermissionDto)0)]
    [InlineData((SharePermissionDto)99)]
    public void NonEditingPermission_OnTheEditRoute_ShowsTheViewOnlyTextAndNoFormNoSaveNoReveal(SharePermissionDto permission)
    {
        ReturnsShared(permission);

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-editor-blocked");
        Assert.Contains(SharedSecretEditor.ViewOnlyText, cut.Find("#shared-secret-editor-blocked").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#shared-secret-save"));
        Assert.Empty(cut.FindAll("#secret-name-input"));
        Assert.Empty(cut.FindAll("#secret-change-notes"));
        Assert.EndsWith(SharingRoutes.Secret(OwnerId, SecretId), cut.Find("#shared-secret-cancel").GetAttribute("href"), StringComparison.Ordinal);
        _ = SharedItemsClient.DidNotReceiveWithAnyArgs().RevealSecretAsync(default, default, default!, Xunit.TestContext.Current.CancellationToken);
        _ = SharedItemsClient.DidNotReceiveWithAnyArgs().UpdateSecretAsync(default, default, default!, default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(SharePermissionDto.Edit)]
    [InlineData(SharePermissionDto.Manage)]
    public void Damaged_EvenForAnEditor_ShowsTheDamagedTextAndNoForm(SharePermissionDto permission)
    {
        ReturnsShared(permission, damaged: true);

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-editor-blocked");
        Assert.Contains(SecretEditor.DamagedText, cut.Find("#shared-secret-editor-blocked").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#shared-secret-save"));
    }

    [Theory]
    [InlineData(SharePermissionDto.Edit)]
    [InlineData(SharePermissionDto.Manage)]
    public void EditingPermission_ShowsTheFormWithNoFolderPickerAndNoValuePrefilled(SharePermissionDto permission)
    {
        ReturnsShared(permission);

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-save");
        Assert.Equal("Router login", cut.Find("#secret-name-input").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#secret-folder-select"));
        Assert.Empty(cut.FindAll("#shared-secret-editor-blocked"));
        Assert.DoesNotContain(NotesText, cut.Markup, StringComparison.Ordinal);
        _ = SharedItemsClient.DidNotReceiveWithAnyArgs().RevealSecretAsync(default, default, default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task EditingNotes_AccessEnded_ShowsTheAccessEndedAlertAndHoldsNoNotes()
    {
        ReturnsShared(SharePermissionDto.Edit);
        SharedItemsClient.RevealSecretAsync(OwnerId, SecretId, Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<RevealedValueDto>.NotFound()));
        var cut = RenderPage();

        await cut.WaitForElement("#secret-change-notes").ChangeAsync(true);

        cut.WaitForElement("#shared-secret-access-ended");
        Assert.Null(ModelOf(cut).Notes);
        Assert.Empty(cut.FindAll("#shared-secret-save"));
    }

    [Fact]
    public async Task Save_SignInRequired_RedirectsToSignInAndDoesNotLeaveTheEditor()
    {
        ReturnsShared(SharePermissionDto.Edit);
        UpdateReturns(ApiResponse<SecretDto>.SignInRequired());
        var cut = RenderPage();

        await cut.WaitForElement("#shared-secret-save").ClickAsync();

        cut.WaitForAssertion(() =>
        {
            var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
            Assert.StartsWith(AccountRoutes.SignIn, navigation.History.First().Uri, StringComparison.Ordinal);
        });
        Assert.Empty(cut.FindAll("#shared-secret-access-ended"));
    }

    [Fact]
    public async Task Save_Failed_ShowsAGenericErrorKeepsTheEditsAndStaysOnTheForm()
    {
        ReturnsShared(SharePermissionDto.Edit);
        UpdateReturns(ApiResponse<SecretDto>.Failed());
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-save");
        cut.Find("#secret-name-input").Input("Kept name");

        await cut.Find("#shared-secret-save").ClickAsync();

        cut.WaitForElement("#shared-secret-form-error");
        Assert.DoesNotContain(SharedItemStatusText.AccessEnded, cut.Find("#shared-secret-form-error").TextContent, StringComparison.Ordinal);
        Assert.Equal("Kept name", cut.Find("#secret-name-input").GetAttribute("value"));
        Assert.Empty(cut.FindAll("#shared-secret-access-ended"));
    }

    [Fact]
    public async Task Save_BlankName_ShowsTheValidationTextAndSendsNothing()
    {
        ReturnsShared(SharePermissionDto.Edit);
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-save");
        cut.Find("#secret-name-input").Input("   ");

        await cut.Find("#shared-secret-save").ClickAsync();

        cut.WaitForElement("#shared-secret-form-error");
        Assert.Contains(SecretEditor.NameRequiredText, cut.Find("#shared-secret-form-error").TextContent, StringComparison.Ordinal);
        _ = SharedItemsClient.DidNotReceiveWithAnyArgs().UpdateSecretAsync(default, default, default!, default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Save_ConflictThenReload_LoadsTheFreshItemAndClearsTheConflict()
    {
        ReturnsShared(SharePermissionDto.Edit);
        UpdateReturns(ApiResponse<SecretDto>.Conflict());
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-save");
        cut.Find("#secret-name-input").Input("Mine");
        await cut.Find("#shared-secret-save").ClickAsync();
        await cut.WaitForElement("#shared-secret-conflict-reload").ClickAsync();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#shared-secret-conflict")));
        Assert.Equal("Router login", cut.Find("#secret-name-input").GetAttribute("value"));
        _ = SharedItemsClient.Received(2).GetSecretAsync(OwnerId, SecretId, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Save_SendsNoValueOrNotesUnlessReplacementWasChosen()
    {
        ReturnsShared(SharePermissionDto.Edit);
        var sent = new List<UpdateSecretRequestDto>();
        SharedItemsClient.UpdateSecretAsync(OwnerId, SecretId, Arg.Any<string>(), Arg.Do<UpdateSecretRequestDto>(sent.Add), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretDto>.Success(new SecretDto { Id = SecretId })));
        var cut = RenderPage();

        await cut.WaitForElement("#shared-secret-save").ClickAsync();

        cut.WaitForAssertion(() => Assert.Single(sent));
        Assert.False(sent[0].ReplaceValue);
        Assert.False(sent[0].ReplaceNotes);
        Assert.Null(sent[0].Value);
        Assert.Null(sent[0].Notes);
    }

    [Fact]
    public void Page_RouteChange_ReloadsTheEditorForTheNewIds()
    {
        ReturnsShared(SharePermissionDto.Edit);
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-save");
        var otherOwner = Guid.NewGuid();
        var otherSecret = Guid.NewGuid();
        SharedItemsClient.GetSecretAsync(otherOwner, otherSecret, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedSecretDto>.Success(new SharedSecretDto
            {
                OwnerId = otherOwner,
                OwnerDisplayName = "Other",
                Permission = SharePermissionDto.Edit,
                Item = new SecretDto { Id = otherSecret, Name = "Second", Type = SecretTypeDto.Login, ConcurrencyToken = "etag-second" },
            })));

        cut.Render(parameters => parameters
            .Add(page => page.OwnerId, otherOwner)
            .Add(page => page.SecretId, otherSecret));

        cut.WaitForAssertion(() => Assert.Equal("Second", cut.Find("#secret-name-input").GetAttribute("value")));
    }
}
