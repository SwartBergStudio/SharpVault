// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class SecretListPanelTests : VaultTestContext
{
    private const string RevealedValue = "correct-horse-battery-staple-7731";

    private readonly Fixture _fixture = new();

    private SecretDto Secret(string? name = null, bool hasValue = true, bool damaged = false, string? username = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Type = SecretTypeDto.Login,
            Name = name ?? $"name-{_fixture.Create<Guid>():N}",
            Username = username,
            HasValue = hasValue,
            IsDamaged = damaged,
            ConcurrencyToken = "etag",
        };

    private static SecretPageDto Page(string? token, params SecretDto[] items) =>
        new() { Items = [.. items], ContinuationToken = token };

    private void ListReturns(Func<string?, ApiResponse<SecretPageDto>> byToken) =>
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(byToken(call.ArgAt<string?>(4))));

    private void ListReturns(SecretPageDto page) =>
        ListReturns(_ => ApiResponse<SecretPageDto>.Success(page));

    private void RevealReturns(ApiResponse<RevealedValueDto> response) =>
        SecretsClient
            .RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

    private IRenderedComponent<SecretListPanel> RenderPanel(Guid? folderId = null, bool unfiled = false, string? search = null) =>
        Render<SecretListPanel>(parameters =>
        {
            parameters
                .Add(panel => panel.FolderId, folderId)
                .Add(panel => panel.Unfiled, unfiled);
            if (search is not null)
            {
                parameters.Add(panel => panel.Search, search);
            }
        });

    private int ListCalls() =>
        SecretsClient.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(SecretsClient.ListSecretsAsync));

    private static string[] RowIds(IRenderedComponent<SecretListPanel> cut) =>
        [.. cut.FindAll("a[id^='vault-secret-link-']").Select(link => link.Id ?? string.Empty)];

    private static string LinkId(SecretDto secret) => $"vault-secret-link-{secret.Id:D}";

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    [Fact]
    public void Render_LoadsFirstPageAndShowsRowsWithIds()
    {
        var first = Secret("Alpha", username: "ada@example.test");
        var second = Secret("Bravo", hasValue: false);
        ListReturns(Page(null, first, second));

        var cut = RenderPanel();

        var table = cut.Find("#vault-secret-list");
        var link = cut.Find($"#{LinkId(first)}");
        Assert.Equal("Alpha", link.TextContent.Trim());
        Assert.Equal(VaultRoutes.Secret(first.Id), link.GetAttribute("href"));
        Assert.NotNull(cut.Find($"#vault-secret-copy-{first.Id:D}"));
        Assert.Contains("ada@example.test", table.TextContent, StringComparison.Ordinal);
        Assert.Contains("Login", table.TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#{LinkId(second)}"));
        Assert.Empty(cut.FindAll("#vault-load-more"));
        Assert.Empty(cut.FindAll("#vault-status"));
    }

    [Fact]
    public void Render_PassesFolderAndPageSizeToTheClient()
    {
        var folder = Guid.NewGuid();
        ListReturns(Page(null, Secret()));

        RenderPanel(folderId: folder);

        SecretsClient.Received(1).ListSecretsAsync(folder, false, null, SecretListPanel.BrowsePageSize, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Unfiled_PassesTheUnfiledFlag()
    {
        ListReturns(Page(null, Secret()));

        RenderPanel(unfiled: true);

        SecretsClient.Received(1).ListSecretsAsync(null, true, null, SecretListPanel.BrowsePageSize, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Empty_ShowsVaultSecretsEmpty()
    {
        ListReturns(Page(null));

        var cut = RenderPanel();

        Assert.Contains("No items here yet", cut.Find("#vault-secrets-empty").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#vault-secret-list"));
    }

    [Fact]
    public void Render_Empty_ShowsAHint()
    {
        ListReturns(Page(null));

        var cut = RenderPanel();

        Assert.NotNull(cut.Find("#vault-secrets-empty-description"));
    }

    [Fact]
    public void Render_EmptyFolder_ShowsAHint()
    {
        ListReturns(Page(null));

        var cut = RenderPanel(folderId: Guid.NewGuid());

        Assert.NotNull(cut.Find("#vault-secrets-empty-description"));
    }

    [Fact]
    public void Render_EmptySearch_HintSuggestsChangingTheSearchNotCreating()
    {
        ListReturns(Page(null));

        var cut = RenderPanel(search: "zzz");

        var hint = cut.Find("#vault-secrets-empty-description").TextContent;
        Assert.Equal("Try a different search term, or select Clear to see all items.", hint);
        Assert.DoesNotContain("New item", hint, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_EmptySearch_ShowsNoMatchText()
    {
        ListReturns(Page(null));

        var cut = RenderPanel(search: "zzz");

        Assert.Contains("No items match your search", cut.Find("#vault-secrets-empty").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Failed_ShowsErrorAndRetryReloads()
    {
        var item = Secret("Alpha");
        var calls = 0;
        ListReturns(_ => ++calls == 1 ? ApiResponse<SecretPageDto>.Failed() : ApiResponse<SecretPageDto>.Success(Page(null, item)));

        var cut = RenderPanel();

        Assert.Contains("could not be loaded", cut.Find("#vault-secrets-error").TextContent, StringComparison.Ordinal);
        cut.Find("#vault-secrets-retry").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#{LinkId(item)}")));
        Assert.Empty(cut.FindAll("#vault-secrets-error"));
    }

    [Fact]
    public void Render_ClientThrows_ShowsGenericErrorWithoutTheMessage()
    {
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ApiResponse<SecretPageDto>>>(_ => throw new InvalidOperationException(RevealedValue));

        var cut = RenderPanel();

        Assert.NotNull(cut.Find("#vault-secrets-error"));
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(RevealedValue, LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_LoadsOnePageWithSearch()
    {
        ListReturns(Page("next-token", Secret("Alpha")));

        var cut = RenderPanel(search: "  alp  ");

        Assert.Equal(1, ListCalls());
        SecretsClient.Received(1).ListSecretsAsync(null, false, "alp", SecretListPanel.SearchPageSize, null, Arg.Any<CancellationToken>());
        Assert.NotNull(cut.Find("#vault-load-more"));
    }

    [Fact]
    public void Search_Whitespace_IsABrowseWithNoSearchTerm()
    {
        ListReturns(Page(null, Secret("Alpha")));

        RenderPanel(search: "   ");

        SecretsClient.Received(1).ListSecretsAsync(null, false, null, SecretListPanel.BrowsePageSize, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Search_EmptyPageWithToken_KeepsLoadingUntilItemsArrive()
    {
        var found = Secret("Needle");
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1")),
            "t1" => ApiResponse<SecretPageDto>.Success(Page("t2")),
            _ => ApiResponse<SecretPageDto>.Success(Page("t3", found)),
        });

        var cut = RenderPanel(search: "needle");

        Assert.Equal(3, ListCalls());
        Assert.NotNull(cut.Find($"#{LinkId(found)}"));
        Assert.NotNull(cut.Find("#vault-load-more"));
    }

    [Fact]
    public void FolderView_LoadsUntilTokenIsNull()
    {
        var first = Secret("A");
        var second = Secret("B");
        var third = Secret("C");
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1", first)),
            "t1" => ApiResponse<SecretPageDto>.Success(Page("t2", second)),
            _ => ApiResponse<SecretPageDto>.Success(Page(null, third)),
        });

        var cut = RenderPanel(folderId: Guid.NewGuid());

        Assert.Equal(3, ListCalls());
        Assert.Equal([LinkId(first), LinkId(second), LinkId(third)], RowIds(cut));
        Assert.Empty(cut.FindAll("#vault-load-more"));
    }

    [Fact]
    public void FolderView_EmptyPageWithToken_KeepsLoading()
    {
        var found = Secret("Late");
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1")),
            _ => ApiResponse<SecretPageDto>.Success(Page(null, found)),
        });

        var cut = RenderPanel();

        Assert.Equal(2, ListCalls());
        Assert.NotNull(cut.Find($"#{LinkId(found)}"));
    }

    [Fact]
    public void FolderView_StopsAt1000AndOffersLoadMore()
    {
        ListReturns(token => ApiResponse<SecretPageDto>.Success(
            Page($"after-{token}", [.. Enumerable.Range(0, SecretListPanel.BrowsePageSize).Select(_ => Secret(hasValue: false))])));

        var cut = RenderPanel(folderId: Guid.NewGuid());

        Assert.Equal(SecretListPanel.BrowseAutoLoadLimit / SecretListPanel.BrowsePageSize, ListCalls());
        Assert.Equal(SecretListPanel.BrowseAutoLoadLimit, RowIds(cut).Length);
        Assert.NotNull(cut.Find("#vault-load-more"));
    }

    [Theory]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.RateLimited)]
    [InlineData(ApiCallStatus.NotFound)]
    [InlineData(ApiCallStatus.ConfigurationMismatch)]
    [InlineData(ApiCallStatus.Invalid)]
    [InlineData(ApiCallStatus.Conflict)]
    public async Task LoadMore_AnyFailureStatus_KeepsTheLoadedRowsAndShowsTheFailure(ApiCallStatus status)
    {
        var first = Secret("Alpha");
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("resume-here", first)),
            _ => new ApiResponse<SecretPageDto>(status, null),
        });
        var cut = RenderPanel(search: "a");

        await cut.Find("#vault-load-more").ClickAsync();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#vault-load-more-error")));
        Assert.Equal([LinkId(first)], RowIds(cut));
        Assert.NotNull(cut.Find("#vault-load-more"));
    }

    [Fact]
    public void LoadMore_CallsWithContinuationToken()
    {
        var first = Secret("Alpha");
        var second = Secret("Zulu");
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("resume-here", first)),
            _ => ApiResponse<SecretPageDto>.Success(Page(null, second)),
        });
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#{LinkId(second)}")));
        SecretsClient.Received(1).ListSecretsAsync(null, false, "a", SecretListPanel.SearchPageSize, "resume-here", Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll("#vault-load-more"));
        Assert.Equal([LinkId(first), LinkId(second)], RowIds(cut));
    }

    [Fact]
    public void LoadMore_EmptyPage_HidesButton()
    {
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1", Secret("Alpha"))),
            _ => ApiResponse<SecretPageDto>.Success(Page(null)),
        });
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#vault-load-more")));
        Assert.Single(RowIds(cut));
    }

    [Fact]
    public void LoadMore_Failure_KeepsRowsAndShowsErrorAndButton()
    {
        var first = Secret("Alpha");
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1", first)),
            _ => ApiResponse<SecretPageDto>.Failed(),
        });
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#vault-load-more-error")));
        Assert.NotNull(cut.Find($"#{LinkId(first)}"));
        Assert.NotNull(cut.Find("#vault-load-more"));
    }

    [Fact]
    public void LoadMore_Throws_ShowsErrorAndLogsTypeOnly()
    {
        var calls = 0;
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? Task.FromResult(ApiResponse<SecretPageDto>.Success(Page("t1", Secret("Alpha"))))
                : throw new InvalidOperationException(RevealedValue));
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#vault-load-more-error")));
        Assert.DoesNotContain(RevealedValue, LogText, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), LogText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void LoadMore_SignInRequired_NavigatesToSignIn()
    {
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1", Secret("Alpha"))),
            _ => ApiResponse<SecretPageDto>.SignInRequired(),
        });
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.Contains("/account/sign-in", CurrentUri, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#vault-load-more-error"));
    }

    [Fact]
    public void CopyFromList_CallsRevealWithCopyIntentAndWritesClipboard()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        RevealReturns(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = RevealedValue }));
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.Contains(SecretStatusText.Copied(Settings), cut.Find("#vault-status").TextContent, StringComparison.Ordinal));
        SecretsClient.Received(1).RevealSecretAsync(
            item.Id,
            Arg.Is<RevealSecretRequestDto>(request => request.Field == RevealFieldDto.Value && request.Intent == RevealIntentDto.Copy),
            Arg.Any<CancellationToken>());
        Assert.Equal([RevealedValue], Clipboard.Writes);
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(RevealedValue, LogText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyFromList_ListLoad_NeverRevealsAnything()
    {
        ListReturns(Page(null, Secret("Alpha"), Secret("Bravo")));

        RenderPanel();

        SecretsClient.DidNotReceive().RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>());
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    public void CopyFromList_AfterClipboardDelay_ClipboardIsCleared()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        RevealReturns(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = RevealedValue }));
        var cut = RenderPanel();
        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();
        cut.WaitForAssertion(() => Assert.Single(Clipboard.Writes));

        Services.GetRequiredService<FakeTimeProvider>().Advance(Settings.ClipboardClearDelay);

        cut.WaitForAssertion(() => Assert.Equal(1, Clipboard.ClearCalls));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyFromList_RateLimited_ShowsMessageAndNoClipboardWrite()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        RevealReturns(ApiResponse<RevealedValueDto>.RateLimited());
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.Contains(SecretListPanel.CopyFailedText, cut.Find("#vault-status").TextContent, StringComparison.Ordinal));
        Assert.Empty(Clipboard.Writes);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(ApiCallStatus.NotFound)]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.Conflict)]
    [InlineData(ApiCallStatus.ConfigurationMismatch)]
    public void CopyFromList_RevealFails_ShowsCopyFailedAndNoClipboardWrite(ApiCallStatus status)
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        RevealReturns(new ApiResponse<RevealedValueDto>(status, null));
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.Contains(SecretListPanel.CopyFailedText, cut.Find("#vault-status").TextContent, StringComparison.Ordinal));
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyFromList_ClipboardFails_ShowsCopyFailed()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        RevealReturns(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = RevealedValue }));
        Clipboard.FailWritesWithJavaScriptError(RevealedValue);
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.Contains(SecretListPanel.CopyFailedText, cut.Find("#vault-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(SecretStatusText.Copied(Settings), cut.Markup, StringComparison.Ordinal);
        Assert.Empty(Clipboard.Writes);
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(RevealedValue, LogText, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#{LinkId(item)}"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyFromList_ClipboardThrowsUnexpectedException_ShowsCopyFailedAndLogsTypeOnly()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        RevealReturns(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = RevealedValue }));
        Clipboard.WriteFailure = new InvalidOperationException(RevealedValue);
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.Contains(SecretListPanel.CopyFailedText, cut.Find("#vault-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(RevealedValue, LogText, StringComparison.Ordinal);
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), LogText, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyFromList_RevealThrows_ShowsCopyFailedAndLogsTypeOnly()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        SecretsClient
            .RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns<Task<ApiResponse<RevealedValueDto>>>(_ => throw new InvalidOperationException(RevealedValue));
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.Contains(SecretListPanel.CopyFailedText, cut.Find("#vault-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(RevealedValue, LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyFromList_SecretWithoutValue_HasNoCopyButton()
    {
        var item = Secret("Note", hasValue: false);
        ListReturns(Page(null, item));

        var cut = RenderPanel();

        Assert.NotNull(cut.Find($"#{LinkId(item)}"));
        Assert.Empty(cut.FindAll($"#vault-secret-copy-{item.Id:D}"));
    }

    [Fact]
    public void CopyFromList_MixedRows_OnlyRowsWithAValueGetACopyButton()
    {
        var withValue = Secret("Has value");
        var without = Secret("No value", hasValue: false);
        ListReturns(Page(null, withValue, without));

        var cut = RenderPanel();

        Assert.Single(cut.FindAll("button[id^='vault-secret-copy-']"));
        Assert.NotNull(cut.Find($"#vault-secret-copy-{withValue.Id:D}"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyFromList_SignInRequired_NavigatesAndDoesNotCopy()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        RevealReturns(ApiResponse<RevealedValueDto>.SignInRequired());
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.Contains("/account/sign-in", CurrentUri, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#vault-status"));
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyFromList_AccessDenied_InvalidatesAndNavigatesToAccessDenied()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        RevealReturns(ApiResponse<RevealedValueDto>.AccessDenied());
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.EndsWith("/account/access-denied", CurrentUri, StringComparison.Ordinal));
        Accessor.Received().Invalidate();
        Assert.Empty(cut.FindAll("#vault-status"));
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyFromList_SecondClickWhileFirstIsInFlight_RevealsOnce()
    {
        var item = Secret("Alpha");
        ListReturns(Page(null, item));
        var gate = new TaskCompletionSource<ApiResponse<RevealedValueDto>>();
        SecretsClient
            .RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);
        var cut = RenderPanel();

        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();
        cut.Find($"#vault-secret-copy-{item.Id:D}").Click();
        gate.SetResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = RevealedValue }));

        cut.WaitForAssertion(() => Assert.Single(Clipboard.Writes));
        SecretsClient.Received(1).RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SignInRequired_NavigatesToSignInWithReturnUrl()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/vault?q=abc");
        ListReturns(_ => ApiResponse<SecretPageDto>.SignInRequired());

        var cut = RenderPanel(search: "abc");

        var expected = $"http://localhost/account/sign-in?returnUrl={Uri.EscapeDataString("/vault?q=abc&signinRetry=1")}";
        Assert.Equal(expected, CurrentUri);
        Assert.Empty(cut.FindAll("#vault-secret-list"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccessDenied_NavigatesToAccessDeniedAndShowsNoRows()
    {
        ListReturns(_ => ApiResponse<SecretPageDto>.AccessDenied());

        var cut = RenderPanel();

        Assert.EndsWith("/account/access-denied", CurrentUri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#vault-secret-list"));
    }

    [Fact]
    public void DamagedRow_ShowsDamagedMarker()
    {
        var damaged = Secret("Real but unreadable", hasValue: false, damaged: true);
        var healthy = Secret("Fine");
        ListReturns(Page(null, damaged, healthy));

        var cut = RenderPanel();

        Assert.Equal(SecretDisplayName.DamagedName, cut.Find($"#{LinkId(damaged)}").TextContent.Trim());
        Assert.NotNull(cut.Find($"#vault-secret-damaged-{damaged.Id:D}"));
        Assert.Empty(cut.FindAll($"#vault-secret-damaged-{healthy.Id:D}"));
        Assert.DoesNotContain("Real but unreadable", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"#{LinkId(healthy)}"));
    }

    [Fact]
    public void DamagedRow_NullNameAndNullUsername_DoesNotBreakTheList()
    {
        var damaged = new SecretDto { Id = Guid.NewGuid(), IsDamaged = true, Name = null, Username = null };
        var healthy = Secret("Fine");
        ListReturns(Page(null, damaged, healthy));

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll("#vault-secrets-error"));
        Assert.Equal(SecretDisplayName.DamagedName, cut.Find($"#{LinkId(damaged)}").TextContent.Trim());
        Assert.NotNull(cut.Find($"#{LinkId(healthy)}"));
    }

    [Fact]
    public void DamagedRow_BlankNameNotMarkedDamaged_ShowsUnreadableText()
    {
        var blank = Secret("   ");
        ListReturns(Page(null, blank));

        var cut = RenderPanel();

        Assert.Equal(SecretDisplayName.DamagedName, cut.Find($"#{LinkId(blank)}").TextContent.Trim());
    }

    [Fact]
    public void Rows_SortedByNameWithinLoadedItems()
    {
        var zulu = Secret("zulu");
        var alpha = Secret("Alpha");
        var mike = Secret("mike");
        ListReturns(Page(null, zulu, alpha, mike));

        var cut = RenderPanel();

        Assert.Equal([LinkId(alpha), LinkId(mike), LinkId(zulu)], RowIds(cut));
    }

    [Fact]
    public void Rows_AfterLoadMore_AreSortedAcrossBothPages()
    {
        var bravo = Secret("Bravo");
        var alpha = Secret("Alpha");
        var charlie = Secret("Charlie");
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1", bravo, charlie)),
            _ => ApiResponse<SecretPageDto>.Success(Page(null, alpha)),
        });
        var cut = RenderPanel(search: "x");

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.Equal([LinkId(alpha), LinkId(bravo), LinkId(charlie)], RowIds(cut)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Render_NameUsernameWithMarkupAndJavascriptUrl_RendersEncodedAndInert()
    {
        const string hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        var item = Secret(hostile, username: hostile);
        item.Url = "javascript:alert(3)";
        ListReturns(Page(null, item));

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("[onerror]"));
        Assert.Equal(hostile, cut.Find($"#{LinkId(item)}").TextContent.Trim());
        Assert.Equal(2, cut.FindAll("td").Count(cell => cell.TextContent.Trim() == hostile));
        Assert.Contains("&lt;img", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Render_ListOfSecrets_NeverContainsAnInputOrTheUrl()
    {
        var item = Secret("Alpha");
        item.Url = "https://internal.example.test/login";
        ListReturns(Page(null, item));

        var cut = RenderPanel();

        Assert.Empty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("textarea"));
        Assert.DoesNotContain("https://internal.example.test", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_WhileLoading_IgnoresTheLateResponse()
    {
        var gate = new TaskCompletionSource<ApiResponse<SecretPageDto>>();
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(gate.Task);
        var cut = RenderPanel();

        cut.Dispose();
        gate.SetResult(ApiResponse<SecretPageDto>.Success(Page(null, Secret("Late"))));

        Assert.DoesNotContain("Loading failed", LogText, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_EmptyPagesForever_StopsAtTheEmptyPageLimitAndOffersLoadMore()
    {
        ListReturns(token => ApiResponse<SecretPageDto>.Success(Page($"after-{token}")));

        var cut = RenderPanel(search: "nothing");

        Assert.Equal(SecretListPanel.MaxEmptyPagesPerLoad, ListCalls());
        Assert.NotNull(cut.Find("#vault-load-more"));
        Assert.Empty(cut.FindAll("#vault-secrets-empty"));
    }

    [Fact]
    public void LoadMore_WhileItRuns_DisablesTheCopyButtons()
    {
        var first = Secret("Alpha");
        var pending = new TaskCompletionSource<ApiResponse<SecretPageDto>>();
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(ApiResponse<SecretPageDto>.Success(Page("more", first))),
                pending.Task);
        var cut = RenderPanel(search: "a");
        Assert.False(cut.Find($"#vault-secret-copy-{first.Id:D}").HasAttribute("disabled"));

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.True(cut.Find($"#vault-secret-copy-{first.Id:D}").HasAttribute("disabled")));
        pending.SetResult(ApiResponse<SecretPageDto>.Success(Page(null)));
        cut.WaitForAssertion(() => Assert.False(cut.Find($"#vault-secret-copy-{first.Id:D}").HasAttribute("disabled")));
    }

    [Fact]
    public void Copy_WhileItRuns_DisablesLoadMore()
    {
        var first = Secret("Alpha");
        ListReturns(Page("more", first));
        var pending = new TaskCompletionSource<ApiResponse<RevealedValueDto>>();
        SecretsClient
            .RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);
        var cut = RenderPanel(search: "a");
        Assert.False(cut.Find("#vault-load-more").HasAttribute("disabled"));

        cut.Find($"#vault-secret-copy-{first.Id:D}").Click();

        cut.WaitForAssertion(() => Assert.True(cut.Find("#vault-load-more").HasAttribute("disabled")));
        pending.SetResult(new ApiResponse<RevealedValueDto>(ApiCallStatus.Failed, null));
        cut.WaitForAssertion(() => Assert.False(cut.Find("#vault-load-more").HasAttribute("disabled")));
    }

    [Fact]
    public void SecretListPanel_LoadMoreSignInRequired_RedirectsOnce()
    {
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1", Secret("Alpha"))),
            _ => ApiResponse<SecretPageDto>.SignInRequired(),
        });
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
        cut.WaitForAssertion(() => Assert.Single(navigation.History, entry => entry.Uri.StartsWith(AccountRoutes.SignIn, StringComparison.Ordinal)));
        Assert.Empty(cut.FindAll("#vault-load-more-error"));
    }

    [Fact]
    public void SecretListPanel_LoadMoreAccessDenied_GoesToAccessDenied()
    {
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1", Secret("Alpha"))),
            _ => ApiResponse<SecretPageDto>.AccessDenied(),
        });
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
        cut.WaitForAssertion(() => Assert.Contains(navigation.History, entry => entry.Uri == AccountRoutes.AccessDenied && entry.Options.ForceLoad));
        Assert.Empty(cut.FindAll("#vault-load-more-error"));
    }

    [Fact]
    public void SecretListPanel_LoadMoreRateLimited_KeepsRowsAndShowsTheServerText()
    {
        var first = Secret("Alpha");
        ListReturns(token => token switch
        {
            null => ApiResponse<SecretPageDto>.Success(Page("t1", first)),
            _ => ApiResponse<SecretPageDto>.RateLimited(),
        });
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.Contains(LoadableComponentBase<SecretPageDto>.RateLimitedText, cut.Find("#vault-load-more-error").TextContent, StringComparison.Ordinal));
        Assert.Equal([LinkId(first)], RowIds(cut));
        Assert.NotNull(cut.Find("#vault-load-more"));
    }

    [Fact]
    public void SecretListPanel_LoadMoreThrows_KeepsRowsAndShowsTheFailedText()
    {
        var first = Secret("Alpha");
        var calls = 0;
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? Task.FromResult(ApiResponse<SecretPageDto>.Success(Page("t1", first)))
                : throw new InvalidOperationException("boom-detail"));
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#vault-load-more-error").TextContent, StringComparison.Ordinal));
        Assert.Equal([LinkId(first)], RowIds(cut));
        Assert.DoesNotContain("boom-detail", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SecretListPanel_LoadMoreDoubleClick_MakesOneCall()
    {
        var first = Secret("Alpha");
        var second = Secret("Zulu");
        var gate = new TaskCompletionSource<ApiResponse<SecretPageDto>>();
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Is<string?>(token => token == null), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretPageDto>.Success(Page("t1", first))));
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Is<string?>("t1"), Arg.Any<CancellationToken>())
            .Returns(_ => gate.Task);
        var cut = RenderPanel(search: "a");

        cut.Find("#vault-load-more").Click();
        cut.Find("#vault-load-more").Click();
        gate.SetResult(ApiResponse<SecretPageDto>.Success(Page(null, second)));

        cut.WaitForAssertion(() => Assert.Contains($"vault-secret-link-{second.Id:D}", RowIds(cut)));
        SecretsClient.Received(1).ListSecretsAsync(null, false, "a", SecretListPanel.SearchPageSize, "t1", Arg.Any<CancellationToken>());
    }
}
