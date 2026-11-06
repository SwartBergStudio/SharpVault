// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class RecycleBinViewTests : VaultTestContext
{
    private const string ContinuationToken = "token-page-two";
    private const string AccessDeniedPath = "/account/access-denied";

    private static readonly DateTimeOffset PurgeAfter = new(2026, 12, 25, 23, 30, 0, TimeSpan.Zero);

    private readonly IRenderedComponent<MudDialogProvider> _provider;

    public RecycleBinViewTests()
    {
        _provider = RenderDialogProviders();
    }

    private static RecycleBinItemDto Item(RecycleBinItemKindDto kind, string? name = "Item", bool damaged = false) => new()
    {
        Kind = kind,
        Id = Guid.NewGuid(),
        Name = name,
        Type = kind == RecycleBinItemKindDto.Secret ? SecretTypeDto.Login : null,
        DeletionBatchId = Guid.NewGuid(),
        DeletedAt = PurgeAfter.AddDays(-30),
        PurgeAfter = PurgeAfter,
        IsDamaged = damaged,
    };

    private static RecycleBinPageDto Page(string? token, params RecycleBinItemDto[] items) => new()
    {
        Items = [.. items],
        ContinuationToken = token,
    };

    private static Task<ApiResponse<T>> Reply<T>(ApiResponse<T> response) => Task.FromResult(response);

    private static string RestoreId(RecycleBinItemDto item) => RecycleBinItemText.IdFor("bin-restore", item);

    private static string PurgeId(RecycleBinItemDto item) => RecycleBinItemText.IdFor("bin-purge", item);

    private static string Normalise(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private void ListReturns(RecycleBinPageDto page) =>
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RecycleBinPageDto>.Success(page)));

    private IRenderedComponent<RecycleBinView> RenderView() => Render<RecycleBinView>();

    private int ListCalls() => RecycleBinClient.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(RecycleBinClient.ListAsync));

    private BunitNavigationManager Navigator() => Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());

    [Fact]
    public void Render_ShowsExpiry()
    {
        var item = Item(RecycleBinItemKindDto.Secret, "Bank login");
        ListReturns(Page(null, item));

        var cut = RenderView();

        var expected = $"Expires {PurgeAfter.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC";
        Assert.Equal(expected, cut.Find($"#{RecycleBinItemText.IdFor("bin-expires", item)}").TextContent);
    }

    [Fact]
    public void Render_ShowsExpiryWithoutCultureSeparators()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("ja-JP");
        try
        {
            var item = Item(RecycleBinItemKindDto.Secret, "Bank login");
            ListReturns(Page(null, item));

            var cut = RenderView();

            Assert.DoesNotContain("/", cut.Find($"#{RecycleBinItemText.IdFor("bin-expires", item)}").TextContent, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Render_ShowsNameKindAndRowActions()
    {
        var folder = Item(RecycleBinItemKindDto.Folder, "Finance");
        var secret = Item(RecycleBinItemKindDto.Secret, "Bank login");
        ListReturns(Page(null, folder, secret));

        var cut = RenderView();

        Assert.Equal("Finance", cut.Find($"#{RecycleBinItemText.IdFor("bin-name", folder)}").TextContent);
        Assert.Equal("Bank login", cut.Find($"#{RecycleBinItemText.IdFor("bin-name", secret)}").TextContent);
        Assert.NotNull(cut.Find($"#{RestoreId(folder)}"));
        Assert.NotNull(cut.Find($"#{PurgeId(folder)}"));
        Assert.NotNull(cut.Find($"#{RestoreId(secret)}"));
        Assert.NotNull(cut.Find($"#{PurgeId(secret)}"));
        Assert.Empty(cut.FindAll("#bin-empty"));
    }

    [Fact]
    public void Render_Empty_ShowsBinEmpty()
    {
        ListReturns(Page(null));

        var cut = RenderView();

        Assert.NotNull(cut.Find("#bin-empty"));
        Assert.Empty(cut.FindAll("#bin-list"));
        Assert.Empty(cut.FindAll("#bin-load-more"));
    }

    [Fact]
    public void Render_Empty_HintExplainsTheBinAndTheRetentionPeriodWithoutAnAction()
    {
        ListReturns(Page(null));

        var cut = RenderView();

        var hint = cut.Find("#bin-empty-description").TextContent;
        var days = SharpVault.Application.Features.Vault.Common.VaultLimits.RecycleBinRetention.Days.ToString(CultureInfo.InvariantCulture);
        Assert.Equal($"Items and folders you delete wait here for {days} days before they are removed for good.", hint);
        Assert.Empty(cut.FindAll("#bin-empty a, #bin-empty button"));
    }

    [Fact]
    public void Render_EmptyPageWithToken_OffersLoadMoreAndNotTheEmptyState()
    {
        ListReturns(Page(ContinuationToken));

        var cut = RenderView();

        Assert.Empty(cut.FindAll("#bin-empty"));
        Assert.NotNull(cut.Find("#bin-load-more"));
    }

    [Fact]
    public void Render_NoToken_HidesLoadMore()
    {
        ListReturns(Page(null, Item(RecycleBinItemKindDto.Secret)));

        var cut = RenderView();

        Assert.Empty(cut.FindAll("#bin-load-more"));
    }

    [Fact]
    public void Render_FirstPage_RequestsPageSizeWithoutToken()
    {
        ListReturns(Page(null));

        RenderView();

        RecycleBinClient.Received(1).ListAsync(RecycleBinView.PageSize, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_LoadFailed_ShowsErrorAndRetryReloads()
    {
        var calls = 0;
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(++calls == 1 ? ApiResponse<RecycleBinPageDto>.Failed() : ApiResponse<RecycleBinPageDto>.Success(Page(null, Item(RecycleBinItemKindDto.Secret, "Back again")))));

        var cut = RenderView();
        Assert.NotNull(cut.Find("#bin-error"));

        cut.Find("#bin-retry").Click();

        cut.WaitForAssertion(() => Assert.Contains("Back again", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Render_DamagedRow_ShowsUnreadableItemWithDamagedChipAndBothActions()
    {
        var damaged = Item(RecycleBinItemKindDto.Secret, null, damaged: true);
        ListReturns(Page(null, damaged));

        var cut = RenderView();

        Assert.Equal("Unreadable item", cut.Find($"#{RecycleBinItemText.IdFor("bin-name", damaged)}").TextContent);
        Assert.NotNull(cut.Find($"#bin-damaged-secret-{damaged.Id:D}"));
        Assert.False(cut.Find($"#{RestoreId(damaged)}").HasAttribute("disabled"));
        Assert.False(cut.Find($"#{PurgeId(damaged)}").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_DamagedFolderRow_UsesTheFolderKindInTheDamagedId()
    {
        var damaged = Item(RecycleBinItemKindDto.Folder, "   ", damaged: true);
        ListReturns(Page(null, damaged));

        var cut = RenderView();

        Assert.NotNull(cut.Find($"#bin-damaged-folder-{damaged.Id:D}"));
        Assert.Equal("Unreadable item", cut.Find($"#{RecycleBinItemText.IdFor("bin-name", damaged)}").TextContent);
    }

    [Fact]
    public void Render_HealthyRow_HasNoDamagedChip()
    {
        var item = Item(RecycleBinItemKindDto.Secret, "Fine");
        ListReturns(Page(null, item));

        var cut = RenderView();

        Assert.Empty(cut.FindAll($"#bin-damaged-secret-{item.Id:D}"));
    }

    [Fact]
    public void Damaged_Restore_StillCallsRestore()
    {
        var damaged = Item(RecycleBinItemKindDto.Secret, null, damaged: true);
        ListReturns(Page(null, damaged));
        RecycleBinClient.RestoreSecretAsync(damaged.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<SecretDto>.Success(new SecretDto())));
        var cut = RenderView();

        cut.Find($"#{RestoreId(damaged)}").Click();

        cut.WaitForAssertion(() => RecycleBinClient.Received(1).RestoreSecretAsync(damaged.Id, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Damaged_PurgeAfterConfirm_StillCallsPurge()
    {
        var damaged = Item(RecycleBinItemKindDto.Folder, null, damaged: true);
        ListReturns(Page(null, damaged));
        RecycleBinClient.PurgeFolderAsync(damaged.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<bool>.Success(true)));
        var cut = RenderView();

        cut.Find($"#{PurgeId(damaged)}").Click();
        _provider.WaitForElement("#bin-purge-prompt-confirm").Click();

        cut.WaitForAssertion(() => RecycleBinClient.Received(1).PurgeFolderAsync(damaged.Id, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Purge_Prompt_SaysSharedPeopleLoseAccessForGood()
    {
        var secret = Item(RecycleBinItemKindDto.Secret);
        ListReturns(Page(null, secret));
        var cut = RenderView();

        cut.Find($"#{PurgeId(secret)}").Click();

        var message = _provider.WaitForElement("#bin-purge-prompt-message");
        Assert.Contains("People it is shared with lose access to it for good.", Normalise(message.TextContent), StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_Folder_CallsRestoreFolderOnly()
    {
        var folder = Item(RecycleBinItemKindDto.Folder, "Finance");
        ListReturns(Page(null, folder));
        RecycleBinClient.RestoreFolderAsync(folder.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<FolderDto>.Success(new FolderDto())));
        var cut = RenderView();

        cut.Find($"#{RestoreId(folder)}").Click();

        cut.WaitForAssertion(() => RecycleBinClient.Received(1).RestoreFolderAsync(folder.Id, Arg.Any<CancellationToken>()));
        RecycleBinClient.DidNotReceive().RestoreSecretAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Restore_Secret_CallsRestoreSecretOnlyAndReloadsWithSuccessStatus()
    {
        var secret = Item(RecycleBinItemKindDto.Secret, "Bank");
        ListReturns(Page(null, secret));
        RecycleBinClient.RestoreSecretAsync(secret.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<SecretDto>.Success(new SecretDto())));
        var cut = RenderView();

        cut.Find($"#{RestoreId(secret)}").Click();

        cut.WaitForAssertion(() => Assert.Contains("Secret restored.", cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        RecycleBinClient.DidNotReceive().RestoreFolderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Equal(2, ListCalls());
    }

    [Theory]
    [InlineData(SecretTypeDto.SecureNote, "Secure note")]
    [InlineData(SecretTypeDto.ApiKey, "API key")]
    [InlineData(SecretTypeDto.Login, "Login")]
    public void KindLabel_Secret_UsesTheReadableTypeLabel(SecretTypeDto type, string expected)
    {
        var item = new RecycleBinItemDto { Kind = RecycleBinItemKindDto.Secret, Type = type };

        Assert.Equal(expected, RecycleBinItemText.KindLabel(item));
    }

    [Fact]
    public void Restore_SecretConflict_DoesNotClaimPartialProgress()
    {
        var item = Item(RecycleBinItemKindDto.Secret);
        ListReturns(Page(null, item));
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<SecretDto>.Conflict()));
        var cut = RenderView();

        cut.Find($"#{RestoreId(item)}").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#bin-status")));
        Assert.DoesNotContain("Part of this", cut.Find("#bin-status").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RecycleBinItemKindDto.Folder)]
    [InlineData(RecycleBinItemKindDto.Secret)]
    public void Restore_Conflict_ShowsTryAgainToFinishAndReloads(RecycleBinItemKindDto kind)
    {
        var item = Item(kind);
        ListReturns(Page(null, item));
        RecycleBinClient.RestoreFolderAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<FolderDto>.Conflict()));
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<SecretDto>.Conflict()));
        var cut = RenderView();

        cut.Find($"#{RestoreId(item)}").Click();

        cut.WaitForAssertion(() => Assert.Contains(kind == RecycleBinItemKindDto.Folder ? RecycleBinView.PartialText : RecycleBinView.SecretConflictText, cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, ListCalls());
    }

    [Theory]
    [InlineData(RecycleBinItemKindDto.Folder)]
    [InlineData(RecycleBinItemKindDto.Secret)]
    public void Purge_Conflict_ShowsTryAgainToFinishAndReloads(RecycleBinItemKindDto kind)
    {
        var item = Item(kind);
        ListReturns(Page(null, item));
        RecycleBinClient.PurgeFolderAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<bool>.Conflict()));
        RecycleBinClient.PurgeSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<bool>.Conflict()));
        var cut = RenderView();

        cut.Find($"#{PurgeId(item)}").Click();
        _provider.WaitForElement("#bin-purge-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains(kind == RecycleBinItemKindDto.Folder ? RecycleBinView.PartialText : RecycleBinView.SecretConflictText, cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, ListCalls());
    }

    [Theory]
    [InlineData(RecycleBinItemKindDto.Folder)]
    [InlineData(RecycleBinItemKindDto.Secret)]
    public void Restore_NotFound_ReloadsAndSaysItIsGone(RecycleBinItemKindDto kind)
    {
        var item = Item(kind);
        ListReturns(Page(null, item));
        RecycleBinClient.RestoreFolderAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<FolderDto>.NotFound()));
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<SecretDto>.NotFound()));
        var cut = RenderView();

        cut.Find($"#{RestoreId(item)}").Click();

        cut.WaitForAssertion(() => Assert.Contains("no longer in the recycle bin", cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, ListCalls());
    }

    [Theory]
    [InlineData(RecycleBinItemKindDto.Folder)]
    [InlineData(RecycleBinItemKindDto.Secret)]
    public void Purge_NotFound_ReloadsAndSaysItIsGone(RecycleBinItemKindDto kind)
    {
        var item = Item(kind);
        ListReturns(Page(null, item));
        RecycleBinClient.PurgeFolderAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<bool>.NotFound()));
        RecycleBinClient.PurgeSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<bool>.NotFound()));
        var cut = RenderView();

        cut.Find($"#{PurgeId(item)}").Click();
        _provider.WaitForElement("#bin-purge-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("no longer in the recycle bin", cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, ListCalls());
    }

    [Fact]
    public void Restore_ConflictThenReload_ShowsTheNewList()
    {
        var item = Item(RecycleBinItemKindDto.Secret, "Gone soon");
        var calls = 0;
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RecycleBinPageDto>.Success(++calls == 1 ? Page(null, item) : Page(null))));
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<SecretDto>.Conflict()));
        var cut = RenderView();

        cut.Find($"#{RestoreId(item)}").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll($"#{RestoreId(item)}")));
        Assert.NotNull(cut.Find("#bin-empty"));
    }

    [Fact]
    public void Restore_OtherFailure_ShowsFailedTextAndDoesNotReload()
    {
        var item = Item(RecycleBinItemKindDto.Secret);
        ListReturns(Page(null, item));
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<SecretDto>.Failed()));
        var cut = RenderView();

        cut.Find($"#{RestoreId(item)}").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        Assert.Equal(1, ListCalls());
    }

    [Fact]
    public void Restore_Throws_ShowsFailedTextWithoutLeakingTheMessage()
    {
        var item = Item(RecycleBinItemKindDto.Secret);
        ListReturns(Page(null, item));
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("secret-detail-leak"));
        var cut = RenderView();

        cut.Find($"#{RestoreId(item)}").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("secret-detail-leak", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-detail-leak", LogText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ApiCallStatus.SignInRequired)]
    [InlineData(ApiCallStatus.AccessDenied)]
    public void Restore_SessionStatus_UsesTheSharedPathAndShowsNoStatus(ApiCallStatus status)
    {
        var item = Item(RecycleBinItemKindDto.Secret);
        ListReturns(Page(null, item));
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(new ApiResponse<SecretDto>(status, null)));
        var cut = RenderView();
        var historyBefore = Navigator().History.Count;

        cut.Find($"#{RestoreId(item)}").Click();

        cut.WaitForAssertion(() => Assert.True(Navigator().History.Count > historyBefore));
        var last = Navigator().History.First();
        Assert.True(last.Options.ForceLoad);
        var expectedPrefix = status == ApiCallStatus.SignInRequired ? AccountRoutes.SignIn : AccessDeniedPath;
        Assert.StartsWith(expectedPrefix, last.Uri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#bin-status"));
        Assert.Equal(1, ListCalls());
    }

    [Fact]
    public void Purge_Click_OpensConfirmAndCallsNothing()
    {
        var item = Item(RecycleBinItemKindDto.Secret, "Bank");
        ListReturns(Page(null, item));
        var cut = RenderView();

        cut.Find($"#{PurgeId(item)}").Click();

        Assert.NotNull(_provider.WaitForElement("#bin-purge-prompt-confirm"));
        Assert.NotNull(_provider.Find("#bin-purge-prompt-cancel"));
        RecycleBinClient.DidNotReceive().PurgeSecretAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        RecycleBinClient.DidNotReceive().PurgeFolderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Purge_NoClickOnRow_NeverShowsTheConfirm()
    {
        ListReturns(Page(null, Item(RecycleBinItemKindDto.Secret)));

        RenderView();

        Assert.Empty(_provider.FindAll("#bin-purge-prompt-confirm"));
    }

    [Fact]
    public void Purge_Cancel_CallsNothingAndClosesTheDialog()
    {
        var item = Item(RecycleBinItemKindDto.Folder, "Finance");
        ListReturns(Page(null, item));
        var cut = RenderView();
        cut.Find($"#{PurgeId(item)}").Click();

        _provider.WaitForElement("#bin-purge-prompt-cancel").Click();

        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#bin-purge-prompt-confirm")));
        RecycleBinClient.DidNotReceive().PurgeFolderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        RecycleBinClient.DidNotReceive().PurgeSecretAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Equal(1, ListCalls());
    }

    [Fact]
    public void Purge_ConfirmSecret_CallsPurgeSecretOnceAndReloads()
    {
        var item = Item(RecycleBinItemKindDto.Secret, "Bank");
        ListReturns(Page(null, item));
        RecycleBinClient.PurgeSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<bool>.Success(true)));
        var cut = RenderView();
        cut.Find($"#{PurgeId(item)}").Click();

        _provider.WaitForElement("#bin-purge-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("Secret deleted forever.", cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        RecycleBinClient.Received(1).PurgeSecretAsync(item.Id, Arg.Any<CancellationToken>());
        RecycleBinClient.DidNotReceive().PurgeFolderAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        Assert.Equal(2, ListCalls());
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#bin-purge-prompt-confirm")));
    }

    [Fact]
    public void Purge_ConfirmFolder_CallsPurgeFolderOnly()
    {
        var item = Item(RecycleBinItemKindDto.Folder, "Finance");
        ListReturns(Page(null, item));
        RecycleBinClient.PurgeFolderAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<bool>.Success(true)));
        var cut = RenderView();
        cut.Find($"#{PurgeId(item)}").Click();

        _provider.WaitForElement("#bin-purge-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains("Folder deleted forever.", cut.Find("#bin-status").TextContent, StringComparison.Ordinal));
        RecycleBinClient.Received(1).PurgeFolderAsync(item.Id, Arg.Any<CancellationToken>());
        RecycleBinClient.DidNotReceive().PurgeSecretAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Purge_Prompt_SaysItCannotBeUndoneAndNamesTheItem()
    {
        var item = Item(RecycleBinItemKindDto.Secret, "Bank");
        ListReturns(Page(null, item));
        var cut = RenderView();

        cut.Find($"#{PurgeId(item)}").Click();

        var message = _provider.WaitForElement("#bin-purge-prompt-message").TextContent;
        Assert.Contains("cannot be undone", message, StringComparison.Ordinal);
        Assert.Contains("deleted forever", message, StringComparison.Ordinal);
        Assert.Contains("Bank", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Purge_Prompt_FolderAndSecretWordingDiffer()
    {
        var folder = Item(RecycleBinItemKindDto.Folder, "Finance");
        var secret = Item(RecycleBinItemKindDto.Secret, "Bank");
        ListReturns(Page(null, folder, secret));
        var cut = RenderView();

        cut.Find($"#{PurgeId(folder)}").Click();
        var folderMessage = _provider.WaitForElement("#bin-purge-prompt-message").TextContent;
        _provider.Find("#bin-purge-prompt-cancel").Click();
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#bin-purge-prompt-message")));
        await WaitForClosedDialogsToSettleAsync();
        cut.Find($"#{PurgeId(secret)}").Click();
        var secretMessage = _provider.WaitForElement("#bin-purge-prompt-message").TextContent;

        Assert.NotEqual(Normalise(folderMessage.Replace("Finance", "X", StringComparison.Ordinal)), Normalise(secretMessage.Replace("Bank", "X", StringComparison.Ordinal)));
        Assert.Contains("deleted together with this folder", folderMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("deleted together with this folder", secretMessage, StringComparison.Ordinal);
        Assert.Contains("history", secretMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Purge_ItemNameWithMarkup_IsEncodedInThePrompt()
    {
        var item = Item(RecycleBinItemKindDto.Secret, "<img src=x onerror=alert(1)>");
        ListReturns(Page(null, item));
        var cut = RenderView();

        cut.Find($"#{PurgeId(item)}").Click();

        _provider.WaitForElement("#bin-purge-prompt-message");
        Assert.Empty(_provider.FindAll("#bin-purge-prompt-message img"));
        Assert.Contains("&lt;img", _provider.Find("#bin-purge-prompt-message").InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadMore_CallsWithContinuationTokenAndAppendsRows()
    {
        var first = Item(RecycleBinItemKindDto.Secret, "First");
        var second = Item(RecycleBinItemKindDto.Folder, "Second");
        ListReturns(Page(ContinuationToken, first));
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RecycleBinPageDto>.Success(Page(null, second))));
        var cut = RenderView();

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#{RestoreId(second)}")));
        Assert.NotNull(cut.Find($"#{RestoreId(first)}"));
        RecycleBinClient.Received(1).ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("#bin-load-more"));
    }

    [Fact]
    public void LoadMore_NextPageHasAnotherToken_SendsTheNewTokenOnTheNextClick()
    {
        ListReturns(Page(ContinuationToken, Item(RecycleBinItemKindDto.Secret)));
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RecycleBinPageDto>.Success(Page("token-page-three", Item(RecycleBinItemKindDto.Secret)))));
        var cut = RenderView();

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => RecycleBinClient.Received(1).ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>()));
        cut.Find("#bin-load-more").Click();
        cut.WaitForAssertion(() => RecycleBinClient.Received(1).ListAsync(RecycleBinView.PageSize, "token-page-three", Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void LoadMore_EmptyPageWithToken_StillOffersLoadMoreAfterwards()
    {
        ListReturns(Page(ContinuationToken));
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RecycleBinPageDto>.Success(Page("token-page-three"))));
        var cut = RenderView();

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => RecycleBinClient.Received(1).ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>()));
        Assert.NotNull(cut.Find("#bin-load-more"));
        Assert.Empty(cut.FindAll("#bin-empty"));
    }

    [Theory]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.NotFound)]
    [InlineData(ApiCallStatus.RateLimited)]
    [InlineData(ApiCallStatus.Conflict)]
    [InlineData(ApiCallStatus.Invalid)]
    [InlineData(ApiCallStatus.ConfigurationMismatch)]
    public void LoadMore_Failure_ShowsErrorAndKeepsTheLoadedRows(ApiCallStatus status)
    {
        var first = Item(RecycleBinItemKindDto.Secret, "First");
        ListReturns(Page(ContinuationToken, first));
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(new ApiResponse<RecycleBinPageDto>(status, null)));
        var cut = RenderView();

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#bin-load-more-error")));
        Assert.NotNull(cut.Find($"#{RestoreId(first)}"));
        Assert.NotNull(cut.Find("#bin-load-more"));
        Assert.Empty(cut.FindAll("#bin-error"));
        Assert.Empty(cut.FindAll("#bin-empty"));
    }

    [Fact]
    public void LoadMore_Throws_ShowsFailedTextAndKeepsTheLoadedRows()
    {
        var first = Item(RecycleBinItemKindDto.Secret, "First");
        ListReturns(Page(ContinuationToken, first));
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("boom-detail"));
        var cut = RenderView();

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#bin-load-more-error").TextContent, StringComparison.Ordinal));
        Assert.NotNull(cut.Find($"#{RestoreId(first)}"));
        Assert.DoesNotContain("boom-detail", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadMore_FailureThenRetry_ClearsTheErrorAndAppendsRows()
    {
        var first = Item(RecycleBinItemKindDto.Secret, "First");
        var second = Item(RecycleBinItemKindDto.Secret, "Second");
        ListReturns(Page(ContinuationToken, first));
        var calls = 0;
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(++calls == 1 ? ApiResponse<RecycleBinPageDto>.Failed() : ApiResponse<RecycleBinPageDto>.Success(Page(null, second))));
        var cut = RenderView();
        cut.Find("#bin-load-more").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#bin-load-more-error")));

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#{RestoreId(second)}")));
        Assert.Empty(cut.FindAll("#bin-load-more-error"));
    }

    [Theory]
    [InlineData(ApiCallStatus.SignInRequired)]
    [InlineData(ApiCallStatus.AccessDenied)]
    public void LoadMore_SessionStatus_UsesTheSharedPathAndShowsNoLoadMoreError(ApiCallStatus status)
    {
        ListReturns(Page(ContinuationToken, Item(RecycleBinItemKindDto.Secret)));
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(new ApiResponse<RecycleBinPageDto>(status, null)));
        var cut = RenderView();
        var historyBefore = Navigator().History.Count;

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => Assert.True(Navigator().History.Count > historyBefore));
        var expectedPrefix = status == ApiCallStatus.SignInRequired ? AccountRoutes.SignIn : AccessDeniedPath;
        Assert.StartsWith(expectedPrefix, Navigator().History.First().Uri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#bin-load-more-error"));
    }

    [Fact]
    public void Restore_InFlight_DisablesRowActionsUntilItFinishes()
    {
        var item = Item(RecycleBinItemKindDto.Secret);
        ListReturns(Page(null, item));
        var gate = new TaskCompletionSource<ApiResponse<SecretDto>>();
        RecycleBinClient.RestoreSecretAsync(item.Id, Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        var cut = RenderView();

        cut.Find($"#{RestoreId(item)}").Click();

        cut.WaitForAssertion(() => Assert.True(cut.Find($"#{PurgeId(item)}").HasAttribute("disabled")));
        Assert.True(cut.Find($"#{RestoreId(item)}").HasAttribute("disabled"));
        gate.SetResult(ApiResponse<SecretDto>.Success(new SecretDto()));
        cut.WaitForAssertion(() => Assert.False(cut.Find($"#{RestoreId(item)}").HasAttribute("disabled")));
    }

    [Fact]
    public void Render_TableCellsWithButtons_RenderAtRuntimeAndClicksWork()
    {
        var items = new[] { Item(RecycleBinItemKindDto.Secret, "One"), Item(RecycleBinItemKindDto.Folder, "Two") };
        ListReturns(Page(null, items));
        RecycleBinClient.RestoreFolderAsync(items[1].Id, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<FolderDto>.Success(new FolderDto())));
        var cut = RenderView();

        Assert.Equal(2, cut.FindAll("#bin-list tbody tr").Count);
        cut.Find($"#{RestoreId(items[1])}").Click();

        cut.WaitForAssertion(() => RecycleBinClient.Received(1).RestoreFolderAsync(items[1].Id, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void LoadMore_WhileARowActionRuns_IsDisabled()
    {
        var secret = Item(RecycleBinItemKindDto.Secret, "Bank");
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RecycleBinPageDto>.Success(Page(ContinuationToken, secret))));
        var pending = new TaskCompletionSource<ApiResponse<SecretDto>>();
        RecycleBinClient.RestoreSecretAsync(secret.Id, Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = RenderView();
        Assert.False(cut.Find("#bin-load-more").HasAttribute("disabled"));

        cut.Find($"#{RestoreId(secret)}").Click();

        cut.WaitForAssertion(() => Assert.True(cut.Find("#bin-load-more").HasAttribute("disabled")));
        pending.SetResult(ApiResponse<SecretDto>.Success(new SecretDto()));
        cut.WaitForAssertion(() => Assert.False(cut.Find("#bin-load-more").HasAttribute("disabled")));
    }

    [Fact]
    public void RecycleBinView_LoadMoreSignInRequired_RedirectsOnce()
    {
        ListReturns(Page(ContinuationToken, Item(RecycleBinItemKindDto.Secret)));
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RecycleBinPageDto>.SignInRequired()));
        var cut = RenderView();

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => Assert.Single(Navigator().History, entry => entry.Uri.StartsWith(AccountRoutes.SignIn, StringComparison.Ordinal)));
        RecycleBinClient.Received(1).ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RecycleBinView_LoadMoreRateLimited_KeepsRowsAndShowsTheServerText()
    {
        var first = Item(RecycleBinItemKindDto.Secret, "First");
        ListReturns(Page(ContinuationToken, first));
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RecycleBinPageDto>.RateLimited()));
        var cut = RenderView();

        cut.Find("#bin-load-more").Click();

        cut.WaitForAssertion(() => Assert.Contains(LoadableComponentBase<RecycleBinPageDto>.RateLimitedText, cut.Find("#bin-load-more-error").TextContent, StringComparison.Ordinal));
        Assert.NotNull(cut.Find($"#{RestoreId(first)}"));
        Assert.NotNull(cut.Find("#bin-load-more"));
        Assert.Empty(cut.FindAll("#bin-error"));
    }

    [Fact]
    public void RecycleBinView_LoadMoreDoubleClick_MakesOneCall()
    {
        var first = Item(RecycleBinItemKindDto.Secret, "First");
        var second = Item(RecycleBinItemKindDto.Folder, "Second");
        ListReturns(Page(ContinuationToken, first));
        var gate = new TaskCompletionSource<ApiResponse<RecycleBinPageDto>>();
        RecycleBinClient.ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        var cut = RenderView();

        cut.Find("#bin-load-more").Click();
        cut.Find("#bin-load-more").Click();
        gate.SetResult(ApiResponse<RecycleBinPageDto>.Success(Page(null, second)));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#{RestoreId(second)}")));
        RecycleBinClient.Received(1).ListAsync(RecycleBinView.PageSize, ContinuationToken, Arg.Any<CancellationToken>());
    }
}
