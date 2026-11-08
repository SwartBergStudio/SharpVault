// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

[Trait("Category", "Security")]
public sealed class SecretHistoryViewTests : VaultTestContext
{
    private const string FirstToken = "etag-one";
    private const string SecondToken = "etag-two";
    private const string PreviousValue = "previous-value-hunter2";
    private const string AccessDeniedPath = "/account/access-denied";

    private static readonly DateTimeOffset Replaced = new(2026, 3, 1, 10, 30, 0, TimeSpan.Zero);

    private readonly IRenderedComponent<MudDialogProvider> _provider;
    private readonly Guid _secretId = Guid.NewGuid();
    private readonly SecretVersionDto _older = Version(Replaced.AddDays(-2), 1);
    private readonly SecretVersionDto _newer = Version(Replaced, 2);
    private string _token = FirstToken;

    public SecretHistoryViewTests()
    {
        _provider = RenderDialogProviders();
        SecretsClient.GetSecretAsync(_secretId, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<SecretDto>.Success(Secret("Bank login", _token))));
        HistoryClient.ListVersionsAsync(_secretId, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<SecretVersionListDto>.Success(new SecretVersionListDto { Items = [_newer, _older] })));
        HistoryClient.RevealVersionAsync(_secretId, Arg.Any<Guid>(), Arg.Any<RevealVersionRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = PreviousValue })));
    }

    private static SecretVersionDto Version(DateTimeOffset replacedAt, long revision) => new()
    {
        VersionId = Guid.NewGuid(),
        ReplacedAt = replacedAt,
        Revision = revision,
    };

    private static SecretDto Secret(string? name, string token) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = SecretTypeDto.Login,
        ConcurrencyToken = token,
    };

    private static Task<ApiResponse<T>> Reply<T>(ApiResponse<T> response) => Task.FromResult(response);

    private static string ValuePrefix(SecretVersionDto version) => $"history-value-{version.VersionId:D}";

    private static string RestoreId(SecretVersionDto version) => $"history-restore-{version.VersionId:D}";

    private IRenderedComponent<SecretHistoryView> RenderView() =>
        Render<SecretHistoryView>(parameters => parameters.Add(view => view.SecretId, _secretId));

    private BunitNavigationManager Navigator() => Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());

    private int GetSecretCalls() => SecretsClient.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(SecretsClient.GetSecretAsync));

    private void RestoreReturns(ApiResponse<SecretDto> response) =>
        HistoryClient.RestoreVersionAsync(_secretId, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Reply(response));

    private void OpenRestoreDialog(IRenderedComponent<SecretHistoryView> cut, SecretVersionDto version)
    {
        cut.Find($"#{RestoreId(version)}").Click();
        _provider.WaitForElement("#history-restore-prompt-confirm");
    }

    [Fact]
    public void Render_ListsVersionsWithoutValues()
    {
        var cut = RenderView();

        Assert.Equal("hidden", cut.Find($"#{ValuePrefix(_newer)}-frame").GetAttribute("data-state"));
        Assert.Equal("hidden", cut.Find($"#{ValuePrefix(_older)}-frame").GetAttribute("data-state"));
        Assert.DoesNotContain(PreviousValue, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copy_Succeeds_ShowsTheCopiedStatus()
    {
        var cut = RenderView();

        await cut.Find($"#{ValuePrefix(_newer)}-copy").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(SecretStatusText.Copied(Settings), cut.Find("#history-status").TextContent, StringComparison.Ordinal));
        Assert.Equal([PreviousValue], Clipboard.Writes);
    }

    [Fact]
    public void Render_NeverCallsRevealVersion()
    {
        RenderView();

        HistoryClient.DidNotReceive().RevealVersionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<RevealVersionRequestDto>(), Arg.Any<CancellationToken>());
        SecretsClient.DidNotReceive().RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_NoVersions_ShowsHistoryEmpty()
    {
        HistoryClient.ListVersionsAsync(_secretId, Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<SecretVersionListDto>.Success(new SecretVersionListDto())));

        var cut = RenderView();

        Assert.NotNull(cut.Find("#history-empty"));
        Assert.Empty(cut.FindAll("#history-list"));
    }

    [Fact]
    public void Render_BackLink_PointsAtTheSecret()
    {
        var cut = RenderView();

        Assert.Equal(VaultRoutes.Secret(_secretId), "/" + cut.Find("#history-back").GetAttribute("href")!.TrimStart('/'));
    }

    [Fact]
    public void Render_SecretNotFound_ShowsErrorAndNeverListsVersions()
    {
        SecretsClient.GetSecretAsync(_secretId, Arg.Any<CancellationToken>()).Returns(_ => Reply(ApiResponse<SecretDto>.NotFound()));

        var cut = RenderView();

        Assert.NotNull(cut.Find("#history-error"));
        HistoryClient.DidNotReceive().ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Reveal_CallsVersionRevealWithViewIntentForThatVersionOnly()
    {
        var cut = RenderView();

        cut.Find($"#{ValuePrefix(_older)}-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(PreviousValue, cut.Find($"#{ValuePrefix(_older)}").TextContent, StringComparison.Ordinal));
        HistoryClient.Received(1).RevealVersionAsync(_secretId, _older.VersionId, Arg.Is<RevealVersionRequestDto>(request => request.Intent == RevealIntentDto.View), Arg.Any<CancellationToken>());
        HistoryClient.DidNotReceive().RevealVersionAsync(_secretId, _newer.VersionId, Arg.Any<RevealVersionRequestDto>(), Arg.Any<CancellationToken>());
        Assert.Equal("hidden", cut.Find($"#{ValuePrefix(_newer)}-frame").GetAttribute("data-state"));
    }

    [Fact]
    public void Reveal_Failure_ShowsStatusAndKeepsTheValueHidden()
    {
        HistoryClient.RevealVersionAsync(_secretId, _older.VersionId, Arg.Any<RevealVersionRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RevealedValueDto>.Failed()));
        var cut = RenderView();

        cut.Find($"#{ValuePrefix(_older)}-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#history-status").TextContent, StringComparison.Ordinal));
        Assert.Equal("hidden", cut.Find($"#{ValuePrefix(_older)}-frame").GetAttribute("data-state"));
    }

    [Fact]
    public void Reveal_SignInRequired_UsesTheSharedPath()
    {
        HistoryClient.RevealVersionAsync(_secretId, _older.VersionId, Arg.Any<RevealVersionRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Reply(ApiResponse<RevealedValueDto>.SignInRequired()));
        var cut = RenderView();
        var before = Navigator().History.Count;

        cut.Find($"#{ValuePrefix(_older)}-reveal").Click();

        cut.WaitForAssertion(() => Assert.True(Navigator().History.Count > before));
        Assert.StartsWith(AccountRoutes.SignIn, Navigator().History.First().Uri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#history-status"));
    }

    [Fact]
    public void Restore_ClickOpensConfirmAndCallsNothing()
    {
        var cut = RenderView();

        OpenRestoreDialog(cut, _older);

        HistoryClient.DidNotReceive().RestoreVersionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Restore_Cancel_CallsNothingAndClosesTheDialog()
    {
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);

        _provider.Find("#history-restore-prompt-cancel").Click();

        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#history-restore-prompt-confirm")));
        HistoryClient.DidNotReceive().RestoreVersionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Restore_ConfirmSendsSecretConcurrencyToken()
    {
        RestoreReturns(ApiResponse<SecretDto>.Success(Secret("Bank login", SecondToken)));
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);

        _provider.Find("#history-restore-prompt-confirm").Click();

        cut.WaitForAssertion(() => HistoryClient.Received(1).RestoreVersionAsync(_secretId, _older.VersionId, FirstToken, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Restore_Success_ReloadsAndShowsRestoredStatus()
    {
        RestoreReturns(ApiResponse<SecretDto>.Success(Secret("Bank login", SecondToken)));
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);

        _provider.Find("#history-restore-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains(SecretHistoryView.RestoredText, cut.Find("#history-status").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, GetSecretCalls());
        Assert.Empty(cut.FindAll("#history-conflict"));
    }

    [Fact]
    public void Restore_SecondRestoreAfterReload_SendsTheFreshToken()
    {
        var restoreCalls = 0;
        HistoryClient.RestoreVersionAsync(_secretId, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreCalls++;
                _token = SecondToken;
                return Reply(ApiResponse<SecretDto>.Success(Secret("Bank login", SecondToken)));
            });
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);
        _provider.Find("#history-restore-prompt-confirm").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, restoreCalls));
        cut.WaitForAssertion(() => Assert.Equal(2, GetSecretCalls()));
        cut.WaitForAssertion(() => Assert.False(cut.Find($"#{RestoreId(_newer)}").HasAttribute("disabled")));

        OpenRestoreDialog(cut, _newer);
        _provider.Find("#history-restore-prompt-confirm").Click();

        cut.WaitForAssertion(() => HistoryClient.Received(1).RestoreVersionAsync(_secretId, _newer.VersionId, SecondToken, Arg.Any<CancellationToken>()));
        HistoryClient.Received(1).RestoreVersionAsync(_secretId, _older.VersionId, FirstToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Restore_Conflict_ShowsConflict()
    {
        RestoreReturns(ApiResponse<SecretDto>.Conflict());
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);

        _provider.Find("#history-restore-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.ConflictText, cut.Find("#history-conflict").TextContent, StringComparison.Ordinal));
        Assert.NotNull(cut.Find("#history-conflict-reload"));
        Assert.Empty(cut.FindAll("#history-status"));
    }

    [Fact]
    public void Restore_ConflictThenReload_ClearsTheConflictAndUsesTheFreshToken()
    {
        var restoreCalls = 0;
        HistoryClient.RestoreVersionAsync(_secretId, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                restoreCalls++;
                return Reply(restoreCalls == 1 ? ApiResponse<SecretDto>.Conflict() : ApiResponse<SecretDto>.Success(Secret("Bank login", "etag-three")));
            });
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);
        _provider.Find("#history-restore-prompt-confirm").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#history-conflict-reload")));
        _token = SecondToken;

        cut.Find("#history-conflict-reload").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#history-conflict")));
        Assert.Equal(2, GetSecretCalls());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#{RestoreId(_older)}")));
        OpenRestoreDialog(cut, _older);
        _provider.Find("#history-restore-prompt-confirm").Click();
        cut.WaitForAssertion(() => HistoryClient.Received(1).RestoreVersionAsync(_secretId, _older.VersionId, SecondToken, Arg.Any<CancellationToken>()));
    }

    [Theory]
    [InlineData(ApiCallStatus.NotFound)]
    [InlineData(ApiCallStatus.Failed)]
    public void Restore_OtherFailure_ShowsStatusAndNoConflict(ApiCallStatus status)
    {
        RestoreReturns(new ApiResponse<SecretDto>(status, null));
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);

        _provider.Find("#history-restore-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#history-status")));
        Assert.Empty(cut.FindAll("#history-conflict"));
        Assert.Equal(1, GetSecretCalls());
    }

    [Fact]
    public void Restore_Throws_ShowsFailedTextWithoutLeakingTheMessage()
    {
        HistoryClient.RestoreVersionAsync(_secretId, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("restore-leak-detail"));
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);

        _provider.Find("#history-restore-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.FailedText, cut.Find("#history-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("restore-leak-detail", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("restore-leak-detail", LogText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ApiCallStatus.SignInRequired)]
    [InlineData(ApiCallStatus.AccessDenied)]
    public void Restore_SessionStatus_UsesTheSharedPath(ApiCallStatus status)
    {
        RestoreReturns(new ApiResponse<SecretDto>(status, null));
        var cut = RenderView();
        var before = Navigator().History.Count;
        OpenRestoreDialog(cut, _older);

        _provider.Find("#history-restore-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.True(Navigator().History.Count > before));
        var expected = status == ApiCallStatus.SignInRequired ? AccountRoutes.SignIn : AccessDeniedPath;
        Assert.StartsWith(expected, Navigator().History.First().Uri, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#history-conflict"));
    }

    [Fact]
    public void Restore_InFlight_DisablesRestoreButtons()
    {
        var gate = new TaskCompletionSource<ApiResponse<SecretDto>>();
        HistoryClient.RestoreVersionAsync(_secretId, Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => gate.Task);
        var cut = RenderView();
        OpenRestoreDialog(cut, _older);

        _provider.Find("#history-restore-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.True(cut.Find($"#{RestoreId(_newer)}").HasAttribute("disabled")));
        gate.SetResult(ApiResponse<SecretDto>.Conflict());
        cut.WaitForAssertion(() => Assert.False(cut.Find($"#{RestoreId(_newer)}").HasAttribute("disabled")));
    }
}
