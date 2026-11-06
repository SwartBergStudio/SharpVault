// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class VaultBrowserTests : VaultTestContext
{
    private static readonly Guid FolderA = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid FolderB = Guid.Parse("00000000-0000-0000-0000-0000000000b2");

    private readonly IRenderedComponent<MudDialogProvider> _provider;

    public VaultBrowserTests()
    {
        _provider = RenderDialogProviders();
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto
            {
                Items = [new FolderDto { Id = FolderA, Name = "A" }, new FolderDto { Id = FolderB, Name = "B" }],
            })));
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto())));
        FoldersClient.CreateFolderAsync(Arg.Any<CreateFolderRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderDto>.Success(new FolderDto())));
    }

    private string CurrentUri => Services.GetRequiredService<NavigationManager>().Uri;

    private static SecretDto Secret(string name) =>
        new() { Id = Guid.NewGuid(), Name = name, Type = SecretTypeDto.Login, ConcurrencyToken = "etag" };

    private IRenderedComponent<VaultBrowser> RenderBrowser(Guid? folderId = null, bool unfiled = false, string? search = null) =>
        Render<VaultBrowser>(parameters =>
        {
            parameters
                .Add(browser => browser.FolderId, folderId)
                .Add(browser => browser.Unfiled, unfiled);
            if (search is not null)
            {
                parameters.Add(browser => browser.Search, search);
            }
        });

    private int ListCalls() =>
        SecretsClient.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(SecretsClient.ListSecretsAsync));

    [Fact]
    public void Render_ShowsHeadingSearchNewItemFoldersAndList()
    {
        var cut = RenderBrowser();

        Assert.NotNull(cut.Find("#vault-heading"));
        Assert.NotNull(cut.Find("#vault-search-input"));
        Assert.NotNull(cut.Find("#vault-search-submit"));
        Assert.NotNull(cut.Find("#vault-new-secret"));
        Assert.NotNull(cut.Find("#folder-tree-all"));
        Assert.NotNull(cut.Find("#vault-secrets-empty"));
        Assert.Empty(cut.FindAll("#vault-search-clear"));
    }

    [Fact]
    public void Search_NavigatesWithQuery()
    {
        var cut = RenderBrowser();

        cut.Find("#vault-search-input").Input("payroll");
        cut.Find("#vault-search-form").Submit();

        Assert.Equal($"http://localhost{VaultRoutes.Vault}?q=payroll", CurrentUri);
    }

    [Fact]
    public void Search_TermIsTrimmedAndEscaped()
    {
        var cut = RenderBrowser();

        cut.Find("#vault-search-input").Input("  a b&c=d  ");
        cut.Find("#vault-search-form").Submit();

        Assert.Equal($"http://localhost{VaultRoutes.Vault}?q={Uri.EscapeDataString("a b&c=d")}", CurrentUri);
    }

    [Fact]
    public void Search_BlankTerm_NavigatesToThePlainVault()
    {
        var cut = RenderBrowser();

        cut.Find("#vault-search-input").Input("   ");
        cut.Find("#vault-search-form").Submit();

        Assert.Equal($"http://localhost{VaultRoutes.Vault}", CurrentUri);
    }

    [Fact]
    public void Search_FromAFolder_SearchesTheWholeVault()
    {
        var cut = RenderBrowser(folderId: FolderA);

        cut.Find("#vault-search-input").Input("x");
        cut.Find("#vault-search-form").Submit();

        Assert.Equal($"http://localhost{VaultRoutes.Vault}?q=x", CurrentUri);
    }

    [Fact]
    public void SearchTerm_PrefillsTheInputAndOffersClear()
    {
        var cut = RenderBrowser(search: "payroll");

        Assert.Equal("payroll", cut.Find("#vault-search-input").GetAttribute("value"));
        Assert.NotNull(cut.Find("#vault-search-clear"));
    }

    [Fact]
    public void Clear_NavigatesToThePlainVault()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"{VaultRoutes.Vault}?q=payroll");
        var cut = RenderBrowser(search: "payroll");

        cut.Find("#vault-search-clear").Click();

        Assert.Equal($"http://localhost{VaultRoutes.Vault}", CurrentUri);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SearchInput_IsLimitedToTheSearchMaxLength()
    {
        var cut = RenderBrowser();

        Assert.Equal(VaultSearchBox.SearchMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture), cut.Find("#vault-search-input").GetAttribute("maxlength"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SearchTerm_WithMarkup_RendersEncoded()
    {
        const string hostile = "<img src=x onerror=alert(1)>";

        var cut = RenderBrowser(search: hostile);

        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("[onerror]"));
        Assert.Equal(hostile, cut.Find("#vault-search-input").GetAttribute("value"));
    }

    [Fact]
    public void NewSecret_InFolder_LinksWithFolderId()
    {
        var cut = RenderBrowser(folderId: FolderA);

        Assert.Equal($"{VaultRoutes.NewSecret}?folderId={FolderA:D}", cut.Find("#vault-new-secret").GetAttribute("href"));
    }

    [Fact]
    public void NewSecret_NotInFolder_LinksWithoutFolderId()
    {
        Assert.Equal(VaultRoutes.NewSecret, RenderBrowser().Find("#vault-new-secret").GetAttribute("href"));
        Assert.Equal(VaultRoutes.NewSecret, RenderBrowser(unfiled: true).Find("#vault-new-secret").GetAttribute("href"));
    }

    [Fact]
    public void Render_PassesTheRouteToTheListPanel()
    {
        RenderBrowser(folderId: FolderA, search: "needle");

        SecretsClient.Received(1).ListSecretsAsync(FolderA, false, "needle", SecretListPanel.SearchPageSize, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Render_Unfiled_PassesTheUnfiledFlagAndMarksTheLink()
    {
        var cut = RenderBrowser(unfiled: true);

        SecretsClient.Received(1).ListSecretsAsync(null, true, null, SecretListPanel.BrowsePageSize, null, Arg.Any<CancellationToken>());
        Assert.Equal("page", cut.Find("#folder-tree-unfiled").GetAttribute("aria-current"));
    }

    [Fact]
    public void RouteChange_CreatesNewListPanelAndCancelsTheOldLoad()
    {
        var oldGate = new TaskCompletionSource<ApiResponse<SecretPageDto>>();
        var oldToken = CancellationToken.None;
        var fromNewFolder = Secret("From new folder");
        SecretsClient
            .ListSecretsAsync(FolderA, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                oldToken = call.ArgAt<CancellationToken>(5);
                return oldGate.Task;
            });
        SecretsClient
            .ListSecretsAsync(FolderB, Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto { Items = [fromNewFolder] })));
        var cut = RenderBrowser(folderId: FolderA);

        cut.Render(parameters => parameters.Add(browser => browser.FolderId, FolderB));
        oldGate.SetResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto { Items = [Secret("From old folder")] }));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find($"#vault-secret-link-{fromNewFolder.Id:D}")));
        Assert.True(oldToken.IsCancellationRequested);
        Assert.DoesNotContain("From old folder", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("a[id^='vault-secret-link-']"));
    }

    [Fact]
    public void RouteChange_SearchTermChanges_ReloadsTheListWithTheNewTerm()
    {
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto { Items = [Secret("Row")] })));
        var cut = RenderBrowser(search: "first");

        cut.Render(parameters => parameters.Add(browser => browser.Search, "second"));

        SecretsClient.Received(1).ListSecretsAsync(null, false, "first", SecretListPanel.SearchPageSize, null, Arg.Any<CancellationToken>());
        SecretsClient.Received(1).ListSecretsAsync(null, false, "second", SecretListPanel.SearchPageSize, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Rerender_WithTheSameRoute_DoesNotReloadTheList()
    {
        var cut = RenderBrowser(folderId: FolderA);

        cut.Render(parameters => parameters.Add(browser => browser.FolderId, FolderA));

        Assert.Equal(1, ListCalls());
    }

    [Fact]
    public void FolderCreated_ReloadsTheSecretList()
    {
        var cut = RenderBrowser();
        Assert.Equal(1, ListCalls());

        cut.Find("#vault-new-folder").Click();
        _provider.WaitForElement("#folder-editor-name-input").Input("Operations");
        _provider.WaitForAssertion(() => Assert.False(_provider.Find("#folder-editor-save").HasAttribute("disabled")));
        _provider.Find("#folder-editor-save").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, ListCalls()));
    }

    [Fact]
    public void Browser_CallsNoApiItself_OnlyThroughItsChildren()
    {
        RenderBrowser();

        SecretsClient.DidNotReceive().RevealSecretAsync(Arg.Any<Guid>(), Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>());
        FoldersClient.DidNotReceive().DeleteFolderAsync(Arg.Any<Guid>(), Arg.Any<FolderDeleteModeDto>(), Arg.Any<CancellationToken>());
        Assert.Empty(Clipboard.Writes);
    }
}
