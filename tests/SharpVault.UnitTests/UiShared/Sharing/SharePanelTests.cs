// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Directory;
using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharePanelTests : SharingDialogTestHost
{
    private readonly Guid _itemId = Guid.NewGuid();
    private readonly Guid _adaId = Guid.NewGuid();
    private readonly Guid _graceId = Guid.NewGuid();

    public SharePanelTests()
    {
        GrantsClient.ListSecretGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(_adaId, "Ada Lovelace"), Grant(_graceId, "Grace Hopper")));
        GrantsClient.ListFolderGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(_adaId, "Ada Lovelace")));
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private IRenderedComponent<SharePanel> RenderPanel(SharedItemKindDto kind = SharedItemKindDto.Secret) =>
        Render<SharePanel>(parameters => parameters
            .Add(panel => panel.OwnerId, Owner.UserId)
            .Add(panel => panel.Kind, kind)
            .Add(panel => panel.ItemId, _itemId));

    private static DirectoryUserDto NewPerson(string name = "Grace Hopper") =>
        new() { Id = Guid.NewGuid(), DisplayName = name, Email = "grace@example.test" };

    private int ListCalls() =>
        GrantsClient.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IShareGrantsApiClient.ListSecretGrantsAsync));

    private async Task OpenAddAndPickAsync(IRenderedComponent<SharePanel> cut, DirectoryUserDto found)
    {
        DirectoryClient.SearchUsersAsync("gra", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<DirectoryUserListDto>.Success(new DirectoryUserListDto { Items = [found] })));
        await cut.WaitForElement("#share-add").ClickAsync();
        Provider.WaitForElement("#recipient-search-input").Input("gra");
        await Provider.Find("#recipient-search-form").SubmitAsync();
        await Provider.WaitForElement($"#recipient-result-{found.Id:D}").ClickAsync();
        Provider.WaitForElement("#recipient-selected");
    }

    private void ReturnsCreate(ApiResponse<ShareGrantDto> response) =>
        GrantsClient.CreateSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(response));

    private void ReturnsChange(ApiResponse<ShareGrantDto> response) =>
        GrantsClient.ChangeSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ChangeShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(response));

    private void ReturnsRevoke(ApiResponse<bool> response) =>
        GrantsClient.RevokeSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(response));

    private TaskCompletionSource<ApiResponse<bool>> GatedRevoke()
    {
        var gate = new TaskCompletionSource<ApiResponse<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        GrantsClient.RevokeSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        return gate;
    }

    private void ExpectNoChangeCalls() =>
        GrantsClient.DidNotReceiveWithAnyArgs().ChangeSecretGrantAsync(default, default, default, default!, default!, Xunit.TestContext.Current.CancellationToken);

    private void ExpectNoRevokeCalls() =>
        GrantsClient.DidNotReceiveWithAnyArgs().RevokeSecretGrantAsync(default, default, default, Xunit.TestContext.Current.CancellationToken);

    [Fact]
    public void Render_MixedCanChange_OnlyChangeableRowsGetControls()
    {
        GrantsClient.ListSecretGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(_adaId, "Ada Lovelace", canChange: false), Grant(_graceId, "Grace Hopper")));

        var cut = RenderPanel();

        cut.WaitForElement($"#share-grant-revoke-{_graceId:D}");
        Assert.Empty(cut.FindAll($"#share-grant-revoke-{_adaId:D}"));
        Assert.Equal("span", cut.Find($"#share-grant-permission-{_adaId:D}").TagName.ToLowerInvariant());
        Assert.Equal("select", cut.Find($"#share-grant-permission-{_graceId:D}").TagName.ToLowerInvariant());
    }

    [Fact]
    public void Render_FolderKind_ListsFolderGrantsOnly()
    {
        var cut = RenderPanel(SharedItemKindDto.Folder);

        cut.WaitForElement($"#share-grant-name-{_adaId:D}");
        GrantsClient.Received(1).ListFolderGrantsAsync(Owner.UserId, _itemId, Arg.Any<CancellationToken>());
        GrantsClient.DidNotReceiveWithAnyArgs().ListSecretGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Render_DoesNotSearchTheDirectoryUntilTheDialogIsUsed()
    {
        RenderPanel().WaitForElement("#share-add");

        DirectoryClient.DidNotReceiveWithAnyArgs().SearchUsersAsync(default!, Xunit.TestContext.Current.CancellationToken);
        Assert.Empty(Provider.FindAll("#share-dialog-confirm"));
    }

    [Theory]
    [InlineData(ApiCallStatus.SignInRequired, "/account/sign-in")]
    [InlineData(ApiCallStatus.AccessDenied, "/account/access-denied")]
    [Trait("Category", "Security")]
    public async Task Search_SessionStatus_RedirectsWithoutResultsOrErrors(ApiCallStatus status, string expectedPath)
    {
        DirectoryClient.SearchUsersAsync("gra", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new ApiResponse<DirectoryUserListDto>(status, null)));
        var cut = RenderPanel();
        await cut.WaitForElement("#share-add").ClickAsync();
        Provider.WaitForElement("#recipient-search-input").Input("gra");

        await Provider.Find("#recipient-search-form").SubmitAsync();

        cut.WaitForAssertion(() => Assert.Contains(expectedPath, CurrentUri, StringComparison.Ordinal));
        Assert.Empty(Provider.FindAll("#recipient-error"));
        Assert.Empty(Provider.FindAll("#recipient-results"));
    }

    [Fact]
    public async Task Add_Success_ReloadsShowsStatusAndClosesTheDialog()
    {
        var found = NewPerson();
        ReturnsCreate(ApiResponse<ShareGrantDto>.Success(Grant(found.Id, found.DisplayName)));
        var cut = RenderPanel();
        await OpenAddAndPickAsync(cut, found);

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Shared, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-dialog-confirm")));
        Assert.Equal(2, ListCalls());
        await WaitForClosedDialogsToSettleAsync();
    }

    [Theory]
    [InlineData(SharePermissionDto.Edit)]
    [InlineData(SharePermissionDto.Manage)]
    public async Task Add_ChosenPermission_IsSentOnTheFolderRoute(SharePermissionDto permission)
    {
        var found = NewPerson();
        GrantsClient.CreateFolderGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<ShareGrantDto>.Success(Grant(found.Id, found.DisplayName, permission))));
        var cut = RenderPanel(SharedItemKindDto.Folder);
        await OpenAddAndPickAsync(cut, found);
        Provider.Find("#share-dialog-permission").Change(permission.ToString());

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        cut.WaitForAssertion(() => GrantsClient.Received(1).CreateFolderGrantAsync(
            Owner.UserId,
            _itemId,
            Arg.Is<CreateShareGrantRequestDto>(request => request.RecipientId == found.Id && request.Permission == permission),
            Arg.Any<CancellationToken>()));
        await GrantsClient.DidNotReceiveWithAnyArgs().CreateSecretGrantAsync(default, default, default!, Xunit.TestContext.Current.CancellationToken);
        await WaitForClosedDialogsToSettleAsync();
    }

    [Theory]
    [InlineData(ApiCallStatus.Invalid, "That person cannot receive shares.", "That person cannot receive shares.")]
    [InlineData(ApiCallStatus.Invalid, null, VaultStatusText.InvalidFallbackText)]
    [InlineData(ApiCallStatus.Failed, null, VaultStatusText.FailedText)]
    public async Task Add_RecipientUnavailable_ShowsMessageInDialogAndKeepsItOpen(ApiCallStatus status, string? detail, string expected)
    {
        var found = NewPerson();
        ReturnsCreate(new ApiResponse<ShareGrantDto>(status, null, detail));
        var cut = RenderPanel();
        await OpenAddAndPickAsync(cut, found);

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        Provider.WaitForAssertion(() => Assert.Contains(expected, Provider.Find("#share-dialog-error").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#share-status"));
        Assert.Equal(1, ListCalls());
        Assert.False(Provider.Find("#share-dialog-confirm").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Add_Conflict_ReloadsClosesTheDialogAndShowsConflictText()
    {
        var found = NewPerson();
        ReturnsCreate(ApiResponse<ShareGrantDto>.Conflict());
        var cut = RenderPanel();
        await OpenAddAndPickAsync(cut, found);

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.ConflictText, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-dialog-confirm")));
        Assert.Equal(2, ListCalls());
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Add_Throws_ShowsFailedTextAndNeverTheExceptionMessage()
    {
        const string leaked = "table-row-key-leak";
        var found = NewPerson();
        GrantsClient.CreateSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns<Task<ApiResponse<ShareGrantDto>>>(_ => throw new InvalidOperationException(leaked));
        var cut = RenderPanel();
        await OpenAddAndPickAsync(cut, found);

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        Provider.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, Provider.Find("#share-dialog-error").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(leaked, Provider.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(leaked, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(leaked, LogText, StringComparison.Ordinal);
        Assert.False(Provider.Find("#share-dialog-confirm").HasAttribute("disabled"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Add_SignInRequired_RedirectsAndShowsNoDialogError()
    {
        var found = NewPerson();
        ReturnsCreate(ApiResponse<ShareGrantDto>.SignInRequired());
        var cut = RenderPanel();
        await OpenAddAndPickAsync(cut, found);

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains("/account/sign-in", CurrentUri, StringComparison.Ordinal));
        Assert.Empty(Provider.FindAll("#share-dialog-error"));
        Assert.Empty(cut.FindAll("#share-status"));
        Assert.Equal(1, ListCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Add_AccessDenied_InvalidatesAndNavigatesToAccessDenied()
    {
        var found = NewPerson();
        ReturnsCreate(ApiResponse<ShareGrantDto>.AccessDenied());
        var cut = RenderPanel();
        await OpenAddAndPickAsync(cut, found);

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.EndsWith("/account/access-denied", CurrentUri, StringComparison.Ordinal));
        Accessor.Received().Invalidate();
        Assert.Empty(Provider.FindAll("#share-dialog-error"));
    }

    [Fact]
    public async Task Add_ConfirmClickedWhileRunning_SendsOneRequest()
    {
        var found = NewPerson();
        var gate = new TaskCompletionSource<ApiResponse<ShareGrantDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        GrantsClient.CreateSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CreateShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);
        var cut = RenderPanel();
        await OpenAddAndPickAsync(cut, found);

        var first = Provider.Find("#share-dialog-confirm").ClickAsync();
        Provider.WaitForAssertion(() => Assert.True(Provider.Find("#share-dialog-confirm").HasAttribute("disabled")));
        await Provider.Find("#share-dialog-confirm").ClickAsync();
        await Provider.Find("#share-dialog-confirm").ClickAsync();

        await GrantsClient.Received(1).CreateSecretGrantAsync(Owner.UserId, _itemId, Arg.Any<CreateShareGrantRequestDto>(), Arg.Any<CancellationToken>());
        gate.SetResult(ApiResponse<ShareGrantDto>.Success(Grant(found.Id, found.DisplayName)));
        await first;
        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Shared, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    public async Task Add_CancelAndReopen_StartsWithNoSelectionAndNoOldError()
    {
        var found = NewPerson();
        ReturnsCreate(ApiResponse<ShareGrantDto>.Invalid("Not allowed."));
        var cut = RenderPanel();
        await OpenAddAndPickAsync(cut, found);
        await Provider.Find("#share-dialog-confirm").ClickAsync();
        Provider.WaitForElement("#share-dialog-error");

        await Provider.Find("#share-dialog-cancel").ClickAsync();
        cut.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-dialog-confirm")));
        await WaitForClosedDialogsToSettleAsync();
        await cut.Find("#share-add").ClickAsync();

        Provider.WaitForElement("#share-dialog-confirm");
        Assert.Empty(Provider.FindAll("#share-dialog-error"));
        Assert.Empty(Provider.FindAll("#recipient-selected"));
        Assert.True(Provider.Find("#share-dialog-confirm").HasAttribute("disabled"));
    }

    [Fact]
    public void Change_Success_UsesTheConcurrencyTokenReloadsAndShowsStatus()
    {
        ReturnsChange(ApiResponse<ShareGrantDto>.Success(Grant(_graceId, "Grace Hopper", SharePermissionDto.Manage)));
        var cut = RenderPanel();

        cut.WaitForElement($"#share-grant-permission-{_graceId:D}").Change(nameof(SharePermissionDto.Manage));

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.PermissionChanged, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        GrantsClient.Received(1).ChangeSecretGrantAsync(
            Owner.UserId,
            _itemId,
            _graceId,
            "etag-" + _graceId.ToString("N"),
            Arg.Is<ChangeShareGrantRequestDto>(request => request.Permission == SharePermissionDto.Manage),
            Arg.Any<CancellationToken>());
        Assert.Equal(2, ListCalls());
    }

    [Fact]
    public void Change_OnAFolder_UsesTheFolderRoute()
    {
        GrantsClient.ChangeFolderGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ChangeShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<ShareGrantDto>.Success(Grant(_adaId, "Ada Lovelace", SharePermissionDto.Edit))));
        var cut = RenderPanel(SharedItemKindDto.Folder);

        cut.WaitForElement($"#share-grant-permission-{_adaId:D}").Change(nameof(SharePermissionDto.Edit));

        cut.WaitForAssertion(() => GrantsClient.Received(1).ChangeFolderGrantAsync(Owner.UserId, _itemId, _adaId, "etag-" + _adaId.ToString("N"), Arg.Any<ChangeShareGrantRequestDto>(), Arg.Any<CancellationToken>()));
        ExpectNoChangeCalls();
    }

    [Theory]
    [InlineData(nameof(SharePermissionDto.View))]
    [InlineData("99")]
    public void Change_SameOrUnknownValue_SendsNothing(string value)
    {
        var cut = RenderPanel();

        cut.WaitForElement($"#share-grant-permission-{_graceId:D}").Change(value);

        ExpectNoChangeCalls();
        Assert.Equal(1, ListCalls());
    }

    [Fact]
    public void Change_Conflict_ReloadsAndShowsConflictText()
    {
        ReturnsChange(ApiResponse<ShareGrantDto>.Conflict());
        var cut = RenderPanel();

        cut.WaitForElement($"#share-grant-permission-{_graceId:D}").Change(nameof(SharePermissionDto.Edit));

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.ConflictText, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, ListCalls());
    }

    [Fact]
    public void Change_Failed_ShowsFailedTextAndReloads()
    {
        ReturnsChange(new ApiResponse<ShareGrantDto>(ApiCallStatus.Failed, null));
        var cut = RenderPanel();

        cut.WaitForElement($"#share-grant-permission-{_graceId:D}").Change(nameof(SharePermissionDto.Edit));

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, ListCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Change_Throws_ShowsFailedTextAndNoExceptionMessage()
    {
        const string leaked = "etag-internals";
        GrantsClient.ChangeSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ChangeShareGrantRequestDto>(), Arg.Any<CancellationToken>())
            .Returns<Task<ApiResponse<ShareGrantDto>>>(_ => throw new InvalidOperationException(leaked));
        var cut = RenderPanel();

        cut.WaitForElement($"#share-grant-permission-{_graceId:D}").Change(nameof(SharePermissionDto.Edit));

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(leaked, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(leaked, LogText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Change_SignInRequired_RedirectsWithoutReloadOrStatus()
    {
        ReturnsChange(ApiResponse<ShareGrantDto>.SignInRequired());
        var cut = RenderPanel();

        cut.WaitForElement($"#share-grant-permission-{_graceId:D}").Change(nameof(SharePermissionDto.Edit));

        cut.WaitForAssertion(() => Assert.Contains("/account/sign-in", CurrentUri, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#share-status"));
        Assert.Equal(1, ListCalls());
    }

    [Fact]
    public async Task Revoke_Cancel_SendsNothingAndClosesThePrompt()
    {
        var cut = RenderPanel();
        await cut.WaitForElement($"#share-grant-revoke-{_adaId:D}").ClickAsync();

        await Provider.WaitForElement("#share-revoke-prompt-cancel").ClickAsync();

        cut.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-revoke-prompt-confirm")));
        ExpectNoRevokeCalls();
        Assert.Empty(cut.FindAll("#share-status"));
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    public async Task Revoke_ClickedRecipient_IsTheOnlyOneRevoked()
    {
        ReturnsRevoke(ApiResponse<bool>.Success(true));
        var cut = RenderPanel();
        await cut.WaitForElement($"#share-grant-revoke-{_graceId:D}").ClickAsync();

        await Provider.WaitForElement("#share-revoke-prompt-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Revoked, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        await GrantsClient.Received(1).RevokeSecretGrantAsync(Owner.UserId, _itemId, _graceId, Arg.Any<CancellationToken>());
        await GrantsClient.DidNotReceive().RevokeSecretGrantAsync(Owner.UserId, _itemId, _adaId, Arg.Any<CancellationToken>());
        Assert.Equal(2, ListCalls());
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    public async Task Revoke_OnAFolder_UsesTheFolderRoute()
    {
        GrantsClient.RevokeFolderGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = RenderPanel(SharedItemKindDto.Folder);
        await cut.WaitForElement($"#share-grant-revoke-{_adaId:D}").ClickAsync();

        await Provider.WaitForElement("#share-revoke-prompt-confirm").ClickAsync();

        await GrantsClient.Received(1).RevokeFolderGrantAsync(Owner.UserId, _itemId, _adaId, Arg.Any<CancellationToken>());
        ExpectNoRevokeCalls();
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    public async Task Revoke_DoubleClick_SendsOneRequest()
    {
        var gate = GatedRevoke();
        var cut = RenderPanel();
        await cut.WaitForElement($"#share-grant-revoke-{_adaId:D}").ClickAsync();

        var first = Provider.WaitForElement("#share-revoke-prompt-confirm").ClickAsync();
        Provider.WaitForAssertion(() => Assert.True(Provider.Find("#share-revoke-prompt-confirm").HasAttribute("disabled")));
        await Provider.Find("#share-revoke-prompt-confirm").ClickAsync();
        await Provider.Find("#share-revoke-prompt-confirm").ClickAsync();

        await GrantsClient.Received(1).RevokeSecretGrantAsync(Owner.UserId, _itemId, _adaId, Arg.Any<CancellationToken>());
        gate.SetResult(ApiResponse<bool>.Success(true));
        await first;
        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Revoked, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        await GrantsClient.Received(1).RevokeSecretGrantAsync(Owner.UserId, _itemId, _adaId, Arg.Any<CancellationToken>());
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    public async Task Revoke_DialogIgnoresEscapeWhileBusy()
    {
        var gate = GatedRevoke();
        var cut = RenderPanel();
        await cut.WaitForElement($"#share-grant-revoke-{_adaId:D}").ClickAsync();
        var first = Provider.WaitForElement("#share-revoke-prompt-confirm").ClickAsync();
        Provider.WaitForAssertion(() => Assert.True(Provider.Find("#share-revoke-prompt-confirm").HasAttribute("disabled")));

        await PressEscapeAsync();
        await PollAsync(() => Provider.FindAll("#share-revoke-prompt-confirm").Count > 0);

        Assert.NotEmpty(Provider.FindAll("#share-revoke-prompt-confirm"));
        Assert.Equal(SharedItemStatusText.RevokeWarning("Ada Lovelace"), Provider.Find("#share-revoke-prompt-text").TextContent);
        gate.SetResult(ApiResponse<bool>.Success(true));
        await first;
        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Revoked, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        await GrantsClient.Received(1).RevokeSecretGrantAsync(Owner.UserId, _itemId, _adaId, Arg.Any<CancellationToken>());
        await WaitForClosedDialogsToSettleAsync();
    }

    [Theory]
    [InlineData(ApiCallStatus.Conflict, VaultStatusText.ConflictText)]
    [InlineData(ApiCallStatus.Failed, VaultStatusText.FailedText)]
    public async Task Revoke_Failure_ClosesPromptShowsTextAndReloads(ApiCallStatus status, string expected)
    {
        ReturnsRevoke(new ApiResponse<bool>(status, false));
        var cut = RenderPanel();
        await cut.WaitForElement($"#share-grant-revoke-{_adaId:D}").ClickAsync();

        await Provider.WaitForElement("#share-revoke-prompt-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(expected, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-revoke-prompt-confirm")));
        Assert.Equal(2, ListCalls());
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Revoke_Throws_ClosesPromptAndShowsFailedWithoutTheMessage()
    {
        const string leaked = "grant-row-internals";
        GrantsClient.RevokeSecretGrantAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Task<ApiResponse<bool>>>(_ => throw new InvalidOperationException(leaked));
        var cut = RenderPanel();
        await cut.WaitForElement($"#share-grant-revoke-{_adaId:D}").ClickAsync();

        await Provider.WaitForElement("#share-revoke-prompt-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#share-status").TextContent, StringComparison.Ordinal));
        cut.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-revoke-prompt-confirm")));
        Assert.DoesNotContain(leaked, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(leaked, LogText, StringComparison.Ordinal);
        await WaitForClosedDialogsToSettleAsync();
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Revoke_SignInRequired_RedirectsAndShowsNoRevokedStatus()
    {
        ReturnsRevoke(ApiResponse<bool>.SignInRequired());
        var cut = RenderPanel();
        await cut.WaitForElement($"#share-grant-revoke-{_adaId:D}").ClickAsync();

        await Provider.WaitForElement("#share-revoke-prompt-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains("/account/sign-in", CurrentUri, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#share-status"));
        Assert.Equal(1, ListCalls());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Revoke_AccessDenied_InvalidatesAndNavigatesToAccessDenied()
    {
        ReturnsRevoke(ApiResponse<bool>.AccessDenied());
        var cut = RenderPanel();
        await cut.WaitForElement($"#share-grant-revoke-{_adaId:D}").ClickAsync();

        await Provider.WaitForElement("#share-revoke-prompt-confirm").ClickAsync();

        cut.WaitForAssertion(() => Assert.EndsWith("/account/access-denied", CurrentUri, StringComparison.Ordinal));
        Accessor.Received().Invalidate();
        Assert.Empty(cut.FindAll("#share-status"));
    }
}
