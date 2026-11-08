// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class SecretDetailViewTests : VaultTestContext
{
    private readonly Fixture _fixture = new();
    private readonly IRenderedComponent<MudDialogProvider> _provider;
    private readonly SecretDto _secret;
    private readonly string _value;
    private readonly string _notes;

    public SecretDetailViewTests()
    {
        _provider = RenderDialogProviders();
        _value = "pw-" + _fixture.Create<Guid>().ToString("N");
        _notes = "note-" + _fixture.Create<Guid>().ToString("N");
        _secret = NewSecret();
        ReturnsSecret(_secret);
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto())));
        SecretsClient.RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto
            {
                Value = call.ArgAt<RevealSecretRequestDto>(1).Field == RevealFieldDto.Notes ? _notes : _value,
            })));
    }

    private FakeTimeProvider Time => Services.GetRequiredService<FakeTimeProvider>();

    private NavigationManager Navigator => Services.GetRequiredService<NavigationManager>();

    private static async Task SettleAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
        }
    }

    private SecretDto NewSecret() => new()
    {
        Id = _fixture.Create<Guid>(),
        Type = SecretTypeDto.Login,
        Name = "name-" + _fixture.Create<Guid>().ToString("N"),
        Username = "user-" + _fixture.Create<Guid>().ToString("N"),
        Url = "https://example.test/" + _fixture.Create<Guid>().ToString("N"),
        Tags = ["alpha", "beta"],
        HasValue = true,
        HasNotes = true,
        CreatedAt = new DateTimeOffset(2026, 3, 1, 9, 5, 0, TimeSpan.Zero),
        ModifiedAt = new DateTimeOffset(2026, 3, 2, 10, 30, 0, TimeSpan.Zero),
        ConcurrencyToken = "etag-" + _fixture.Create<Guid>().ToString("N"),
    };

    private void ReturnsSecret(SecretDto secret) =>
        SecretsClient.GetSecretAsync(secret.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(secret)));

    private void ReturnsFolders(params FolderDto[] folders) =>
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto { Items = [.. folders] })));

    private IRenderedComponent<SecretDetailView> RenderDetail()
    {
        var cut = Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));
        cut.WaitForElement("#secret-heading");
        return cut;
    }

    private void AssertNoReveal() =>
        SecretsClient.DidNotReceive().RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>());

    private void AssertRevealedOnce(RevealFieldDto field, RevealIntentDto intent)
    {
        SecretsClient.Received(1).RevealSecretAsync(_secret.Id, Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>());
        SecretsClient.Received(1).RevealSecretAsync(
            _secret.Id,
            Arg.Is<RevealSecretRequestDto>(request => request.Field == field && request.Intent == intent),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NotFound_ShowsGoneMessage()
    {
        SecretsClient.GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.NotFound()));

        var cut = Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));

        cut.WaitForAssertion(() => Assert.Contains("no longer exists", cut.Find("#secret-detail-error").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#secret-fields"));
        Assert.Empty(cut.FindAll("#secret-delete"));
        AssertNoReveal();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Render_ShowsMaskedValueAndNeverCallsReveal()
    {
        var cut = RenderDetail();

        Assert.Equal("hidden", cut.Find("#secret-value-frame").GetAttribute("data-state"));
        Assert.Equal("hidden", cut.Find("#secret-notes-frame").GetAttribute("data-state"));
        Assert.DoesNotContain(_value, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(_notes, cut.Markup, StringComparison.Ordinal);
        AssertNoReveal();
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    public void Render_ItemWithoutValueOrNotes_HasNoRevealControls()
    {
        _secret.HasValue = false;
        _secret.HasNotes = false;

        var cut = RenderDetail();

        Assert.Empty(cut.FindAll("#secret-value-reveal"));
        Assert.Empty(cut.FindAll("#secret-value-copy"));
        Assert.Empty(cut.FindAll("#secret-notes-reveal"));
        Assert.Empty(cut.FindAll("#secret-notes-copy"));
        AssertNoReveal();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RevealValue_CallsRevealOnceWithViewAndShowsValue()
    {
        var cut = RenderDetail();

        cut.Find("#secret-value-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(_value, cut.Find("#secret-value").TextContent, StringComparison.Ordinal));
        AssertRevealedOnce(RevealFieldDto.Value, RevealIntentDto.View);
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void HideValue_RemovesValueFromMarkup()
    {
        var cut = RenderDetail();
        cut.Find("#secret-value-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(_value, cut.Markup, StringComparison.Ordinal));

        cut.Find("#secret-value-hide").Click();

        Assert.DoesNotContain(_value, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("hidden", cut.Find("#secret-value-frame").GetAttribute("data-state"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RevealValue_AfterAutoHideDelay_HidesValueAgain()
    {
        var cut = RenderDetail();
        cut.Find("#secret-value-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(_value, cut.Markup, StringComparison.Ordinal));

        Time.Advance(Settings.RevealAutoHideDelay);
        await SettleAsync();

        cut.WaitForAssertion(() => Assert.DoesNotContain(_value, cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AfterClipboardDelay_ClipboardCleared()
    {
        var cut = RenderDetail();
        cut.Find("#secret-value-copy").Click();
        cut.WaitForAssertion(() => Assert.Equal([_value], Clipboard.Writes));

        Time.Advance(Settings.ClipboardClearDelay - TimeSpan.FromMilliseconds(1));
        await SettleAsync();
        Assert.Equal(0, Clipboard.ClearCalls);

        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Assert.Equal(1, Clipboard.ClearCalls));
        Assert.Equal(string.Empty, Clipboard.Current);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyValue_CallsRevealWithCopyAndWritesClipboardWithoutRendering()
    {
        var cut = RenderDetail();

        cut.Find("#secret-value-copy").Click();

        cut.WaitForAssertion(() => Assert.Equal([_value], Clipboard.Writes));
        AssertRevealedOnce(RevealFieldDto.Value, RevealIntentDto.Copy);
        Assert.DoesNotContain(_value, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("hidden", cut.Find("#secret-value-frame").GetAttribute("data-state"));
        Assert.Contains(SecretStatusText.Copied(Settings), cut.WaitForElement("#secret-status").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CopyValue_TwiceBeforeDelay_FirstTimerDoesNotRemoveTheSecondStatus()
    {
        var cut = RenderDetail();
        await cut.Find("#secret-value-copy").ClickAsync();
        cut.WaitForElement("#secret-status");
        Time.Advance(Settings.ClipboardClearDelay - TimeSpan.FromSeconds(1));
        await cut.Find("#secret-value-copy").ClickAsync();

        Time.Advance(TimeSpan.FromSeconds(1));
        await SettleAsync();

        Assert.NotEmpty(cut.FindAll("#secret-status"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CopyNotes_CallsRevealWithNotesAndCopyAndWritesClipboardWithoutRendering()
    {
        var cut = RenderDetail();

        cut.Find("#secret-notes-copy").Click();

        cut.WaitForAssertion(() => Assert.Equal([_notes], Clipboard.Writes));
        AssertRevealedOnce(RevealFieldDto.Notes, RevealIntentDto.Copy);
        Assert.DoesNotContain(_notes, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RevealNotes_CallsRevealWithNotes()
    {
        var cut = RenderDetail();

        cut.Find("#secret-notes-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(_notes, cut.Find("#secret-notes").TextContent, StringComparison.Ordinal));
        AssertRevealedOnce(RevealFieldDto.Notes, RevealIntentDto.View);
        Assert.DoesNotContain(_value, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RateLimited_ShowsMessageWithoutValue()
    {
        SecretsClient.RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<RevealedValueDto>.RateLimited()));
        var cut = RenderDetail();

        cut.Find("#secret-value-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.For(ApiResponse<bool>.RateLimited()), cut.Find("#secret-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(_value, cut.Markup, StringComparison.Ordinal);
        Assert.Equal("hidden", cut.Find("#secret-value-frame").GetAttribute("data-state"));
    }

    [Fact]
    public void SecureNote_HidesUsernameUrlAndValueRows()
    {
        _secret.Type = SecretTypeDto.SecureNote;
        _secret.Username = null;
        _secret.Url = null;
        _secret.HasValue = false;

        var cut = RenderDetail();

        Assert.Empty(cut.FindAll("#secret-username"));
        Assert.Empty(cut.FindAll("#secret-url"));
        Assert.Empty(cut.FindAll("#secret-value"));
        Assert.Empty(cut.FindAll("#secret-value-reveal"));
        Assert.NotNull(cut.Find("#secret-notes"));
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void SecretDetail_NonHttpUrlFromApi_RendersAsTextNotLink(string url)
    {
        _secret.Url = url;

        var cut = RenderDetail();

        var element = cut.Find("#secret-url");
        Assert.Equal("span", element.TagName.ToLowerInvariant());
        Assert.Equal(url, element.TextContent.Trim());
        Assert.Empty(cut.FindAll("#secret-fields a"));
    }

    [Fact]
    public void HttpsUrl_RendersLinkWithNoopener()
    {
        _secret.Url = "https://example.test/login";

        var cut = RenderDetail();

        var link = cut.Find("a#secret-url");
        Assert.Equal(_secret.Url, link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        var relation = link.GetAttribute("rel") ?? string.Empty;
        Assert.Contains("noopener", relation, StringComparison.Ordinal);
        Assert.Contains("noreferrer", relation, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_ConfirmSendsConcurrencyTokenAndNavigates()
    {
        SecretsClient.DeleteSecretAsync(_secret.Id, _secret.ConcurrencyToken, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = RenderDetail();
        cut.Find("#secret-delete").Click();

        _provider.WaitForElement("#secret-delete-prompt-confirm").Click();

        SecretsClient.Received(1).DeleteSecretAsync(_secret.Id, _secret.ConcurrencyToken, Arg.Any<CancellationToken>());
        Assert.EndsWith(VaultRoutes.Vault, Navigator.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_Cancel_CallsNothing()
    {
        var cut = RenderDetail();
        cut.Find("#secret-delete").Click();

        _provider.WaitForElement("#secret-delete-prompt-cancel").Click();

        SecretsClient.DidNotReceive().DeleteSecretAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Delete_Conflict_ShowsReloadMessage()
    {
        SecretsClient.DeleteSecretAsync(_secret.Id, _secret.ConcurrencyToken, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Conflict()));
        var cut = RenderDetail();
        cut.Find("#secret-delete").Click();

        _provider.WaitForElement("#secret-delete-prompt-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains(VaultStatusText.ConflictText, cut.Find("#secret-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(VaultRoutes.Vault, new Uri(Navigator.Uri).AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Move_ConfirmSendsConcurrencyTokenAndTargetFolder()
    {
        var folder = new FolderDto { Id = _fixture.Create<Guid>(), Name = "Target" };
        ReturnsFolders(folder);
        SecretsClient.MoveSecretAsync(_secret.Id, _secret.ConcurrencyToken, Arg.Any<MoveSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(_secret)));
        var cut = RenderDetail();
        cut.Find("#secret-move").Click();
        _provider.WaitForElement("#move-secret-folder-select").Change(folder.Id.ToString("D"));

        _provider.Find("#move-secret-confirm").Click();

        cut.WaitForAssertion(() => SecretsClient.Received(1).MoveSecretAsync(
            _secret.Id,
            _secret.ConcurrencyToken,
            Arg.Is<MoveSecretRequestDto>(request => request.FolderId == folder.Id),
            Arg.Any<CancellationToken>()));
    }

    [Fact]
    public async Task Reveal_FailureThenSuccess_ClearsTheFailureStatus()
    {
        SecretsClient.RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(ApiResponse<RevealedValueDto>.RateLimited()),
                Task.FromResult(ApiResponse<RevealedValueDto>.Success(new RevealedValueDto { Value = _value })));
        var cut = RenderDetail();
        await cut.Find("#secret-value-reveal").ClickAsync();
        cut.WaitForAssertion(() => Assert.Contains(LoadableComponentBase<bool>.RateLimitedText, cut.Find("#secret-status").TextContent, StringComparison.Ordinal));

        await cut.Find("#secret-value-reveal").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(_value, cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#secret-status"));
    }

    [Fact]
    public void Damaged_ShowsDamagedPanelAndOnlyTheActionsThatStillWork()
    {
        _secret.IsDamaged = true;
        _secret.Name = null;
        _secret.Username = null;
        _secret.Url = null;
        _secret.Tags = [];

        var cut = RenderDetail();

        Assert.Equal(SecretDisplayName.DamagedName, cut.Find("#secret-heading").TextContent.Trim());
        Assert.NotNull(cut.Find("#secret-damaged"));
        Assert.Empty(cut.FindAll("#secret-edit"));
        Assert.Empty(cut.FindAll("#secret-move"));
        Assert.NotNull(cut.Find("#secret-delete"));
        Assert.NotNull(cut.Find("#secret-history-link"));
    }

    [Fact]
    public void CopyUsername_WritesClipboardWithoutAnyRevealCall()
    {
        var cut = RenderDetail();

        cut.Find("#secret-username-copy").Click();

        cut.WaitForAssertion(() => Assert.Equal([_secret.Username!], Clipboard.Writes));
        AssertNoReveal();
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RevealAndCopy_NeverWriteTheValueToTheLogs()
    {
        var cut = RenderDetail();

        cut.Find("#secret-value-reveal").Click();
        cut.WaitForAssertion(() => Assert.Contains(_value, cut.Markup, StringComparison.Ordinal));
        cut.Find("#secret-notes-copy").Click();
        cut.WaitForAssertion(() => Assert.Equal([_notes], Clipboard.Writes));

        Assert.DoesNotContain(_value, LogText, StringComparison.Ordinal);
        Assert.DoesNotContain(_notes, LogText, StringComparison.Ordinal);
    }
}
