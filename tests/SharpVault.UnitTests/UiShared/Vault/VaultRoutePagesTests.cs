// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class VaultRoutePagesTests : VaultTestContext
{
    private const string ItemName = "Payroll login";

    public VaultRoutePagesTests()
    {
        RenderDialogProviders();
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto())));
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto
            {
                Items = [new SecretDto { Id = Guid.NewGuid(), Name = ItemName, Type = SecretTypeDto.Login, ConcurrencyToken = "etag" }],
            })));
    }

    private void NavigateTo(string route) =>
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);

    private int SearchedCalls(string term) =>
        SecretsClient.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(SecretsClient.ListSecretsAsync) && Equals(call.GetArguments()[2], term));

    [Fact]
    public void VaultPage_NoQuery_HasNoSearchValueAndListsTheItems()
    {
        NavigateTo(VaultRoutes.Vault);

        var cut = Render<VaultPage>();

        cut.WaitForAssertion(() => Assert.Contains(ItemName, cut.Markup, StringComparison.Ordinal));
        Assert.True(string.IsNullOrEmpty(cut.Find("#vault-search-input").GetAttribute("value")));
        Assert.Empty(cut.FindAll("#vault-search-clear"));
        Assert.Equal(0, SearchedCalls("Search"));
    }

    [Fact]
    public void VaultPage_QueryPresent_SearchesForThatTerm()
    {
        NavigateTo($"{VaultRoutes.Vault}?q=x");

        var cut = Render<VaultPage>();

        Assert.Equal("x", cut.Find("#vault-search-input").GetAttribute("value"));
        cut.WaitForAssertion(() => Assert.Equal(1, SearchedCalls("x")));
    }

    [Fact]
    public void VaultUnfiledPage_NoQuery_HasNoSearchValueAndListsTheItems()
    {
        NavigateTo(VaultRoutes.Unfiled);

        var cut = Render<VaultUnfiledPage>();

        cut.WaitForAssertion(() => Assert.Contains(ItemName, cut.Markup, StringComparison.Ordinal));
        Assert.True(string.IsNullOrEmpty(cut.Find("#vault-search-input").GetAttribute("value")));
        Assert.Empty(cut.FindAll("#vault-search-clear"));
        Assert.Equal(0, SearchedCalls("Search"));
    }

    [Fact]
    public void VaultUnfiledPage_QueryPresent_SearchesForThatTerm()
    {
        NavigateTo($"{VaultRoutes.Unfiled}?q=x");

        var cut = Render<VaultUnfiledPage>();

        Assert.Equal("x", cut.Find("#vault-search-input").GetAttribute("value"));
        cut.WaitForAssertion(() => Assert.Equal(1, SearchedCalls("x")));
    }
}
