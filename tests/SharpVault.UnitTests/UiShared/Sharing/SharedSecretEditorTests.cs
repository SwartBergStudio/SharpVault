// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedSecretEditorTests : SharedDetailTestHost
{
    private const string NotesText = "note-body-77";

    private readonly List<UpdateSecretRequestDto> _updates = [];

    public SharedSecretEditorTests()
    {
        ReturnsReveal(NotesText);
        SharedItemsClient.UpdateSecretAsync(OwnerId, SecretId, Arg.Any<string>(), Arg.Do<UpdateSecretRequestDto>(_updates.Add), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretDto>.Success(new SecretDto { Id = SecretId })));
    }

    private NavigationManager Navigator => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<SharedSecretEditPage> RenderPage()
    {
        var cut = Render<SharedSecretEditPage>(parameters => parameters
            .Add(page => page.OwnerId, OwnerId)
            .Add(page => page.SecretId, SecretId));
        cut.WaitForElement("#shared-secret-editor-heading");
        return cut;
    }

    private static UpdateSecretRequestDto ModelOf(IRenderedComponent<SharedSecretEditPage> cut) =>
        (UpdateSecretRequestDto)typeof(SharedSecretEditor).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.FindComponent<SharedSecretEditor>().Instance)!;

    [Fact]
    public void Save_SendsTheLoadedTokenToTheSharedRouteAndReturnsToTheItem()
    {
        ReturnsShared(SharePermissionDto.Edit);
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-save");
        cut.Find("#secret-name-input").Input("Renamed");

        cut.Find("#shared-secret-save").Click();

        cut.WaitForAssertion(() => Assert.Single(_updates));
        Assert.Equal("Renamed", _updates[0].Name);
        SharedItemsClient.Received(1).UpdateSecretAsync(OwnerId, SecretId, "etag-shared", Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>());
        Assert.EndsWith(SharingRoutes.Secret(OwnerId, SecretId), Navigator.Uri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#secret-folder-select"));
    }

    [Fact]
    public void Save_Conflict_KeepsTheEditsAndOffersReload()
    {
        ReturnsShared(SharePermissionDto.Edit);
        SharedItemsClient.UpdateSecretAsync(OwnerId, SecretId, Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretDto>.Conflict()));
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-save");
        cut.Find("#secret-name-input").Input("Mine");

        cut.Find("#shared-secret-save").Click();

        cut.WaitForElement("#shared-secret-conflict");
        Assert.Equal("Mine", cut.Find("#secret-name-input").GetAttribute("value"));
        Assert.NotNull(cut.Find("#shared-secret-conflict-reload"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Save_NotFound_ShowsAccessEndedAndKeepsNoValueOrNotes()
    {
        ReturnsShared(SharePermissionDto.Edit);
        SharedItemsClient.UpdateSecretAsync(OwnerId, SecretId, Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretDto>.NotFound()));
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-save");
        cut.Find("#secret-change-notes").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(NotesText, cut.Find("#secret-notes-input").GetAttribute("value") ?? cut.Find("#secret-notes-input").TextContent));

        cut.Find("#shared-secret-save").Click();

        cut.WaitForElement("#shared-secret-access-ended");
        Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-secret-access-ended").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#secret-notes-input"));
        Assert.DoesNotContain(NotesText, cut.Markup, StringComparison.Ordinal);
        Assert.Null(ModelOf(cut).Notes);
        Assert.Empty(cut.FindAll("#shared-secret-save"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void EditingNotes_RevealsThroughTheSharedRouteWithViewIntent()
    {
        ReturnsShared(SharePermissionDto.Edit);
        var cut = RenderPage();
        cut.WaitForElement("#secret-change-notes").Change(true);

        cut.WaitForAssertion(() => Assert.Equal(NotesText, ModelOf(cut).Notes));
        SharedItemsClient.Received(1).RevealSecretAsync(
            OwnerId,
            SecretId,
            Arg.Is<RevealSecretRequestDto>(request => request.Field == RevealFieldDto.Notes && request.Intent == RevealIntentDto.View),
            Arg.Any<CancellationToken>());
        SecretsClient.DidNotReceiveWithAnyArgs().RevealSecretAsync(default, default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Dispose_ScrubsValueAndNotes()
    {
        ReturnsShared(SharePermissionDto.Edit);
        var cut = RenderPage();
        cut.WaitForElement("#secret-change-notes").Change(true);
        cut.WaitForAssertion(() => Assert.Equal(NotesText, ModelOf(cut).Notes));
        var model = ModelOf(cut);
        model.Value = "typed-value";

        await cut.FindComponent<SharedSecretEditor>().Instance.DisposeAsync();

        Assert.Null(model.Value);
        Assert.Null(model.Notes);
    }

    [Fact]
    public void Save_DoubleClick_SendsOneRequest()
    {
        ReturnsShared(SharePermissionDto.Edit);
        var gate = new TaskCompletionSource<ApiResponse<SecretDto>>();
        SharedItemsClient.UpdateSecretAsync(OwnerId, SecretId, Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-save");

        cut.Find("#shared-secret-save").Click();
        cut.Find("#shared-secret-save").Click();

        SharedItemsClient.Received(1).UpdateSecretAsync(OwnerId, SecretId, Arg.Any<string>(), Arg.Any<UpdateSecretRequestDto>(), Arg.Any<CancellationToken>());
        gate.SetResult(ApiResponse<SecretDto>.Success(new SecretDto { Id = SecretId }));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void View_RecipientOnTheEditRoute_GetsNoFormAndNoSave()
    {
        ReturnsShared(SharePermissionDto.View);

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-editor-blocked");
        Assert.Empty(cut.FindAll("#shared-secret-save"));
        Assert.Empty(cut.FindAll("#secret-name-input"));
    }

    [Fact]
    public void Load_NotFound_ShowsAccessEnded()
    {
        SharedItemsClient.GetSecretAsync(OwnerId, SecretId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedSecretDto>.NotFound()));

        var cut = Render<SharedSecretEditPage>(parameters => parameters
            .Add(page => page.OwnerId, OwnerId)
            .Add(page => page.SecretId, SecretId));

        cut.WaitForElement("#shared-secret-editor-error");
        Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-secret-editor-error").TextContent, StringComparison.Ordinal);
    }
}
