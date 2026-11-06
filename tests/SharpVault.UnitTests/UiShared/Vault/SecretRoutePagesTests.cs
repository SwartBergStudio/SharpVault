// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class SecretRoutePagesTests : VaultTestContext
{
    private readonly Guid _firstId = Guid.NewGuid();
    private readonly Guid _secondId = Guid.NewGuid();

    public SecretRoutePagesTests()
    {
        RenderDialogProviders();
        Returns(_firstId, "First secret");
        Returns(_secondId, "Second secret");
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto())));
        HistoryClient.ListVersionsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretVersionListDto>.Success(new SecretVersionListDto
            {
                Items = [new SecretVersionDto { VersionId = Guid.NewGuid(), ReplacedAt = DateTimeOffset.UtcNow, Revision = 1 }],
            })));
    }

    private static string? SelectedFolder(IRenderedComponent<SecretNewPage> cut) =>
        cut.Find("#secret-folder-select").QuerySelector("option[selected]")?.TextContent;

    private void Returns(Guid id, string name) =>
        SecretsClient.GetSecretAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(new SecretDto
            {
                Id = id,
                Name = name,
                Type = SecretTypeDto.Login,
                ConcurrencyToken = "etag-" + name,
            })));

    [Fact]
    public void DetailPage_RouteChangesToAnotherSecret_ShowsTheNewSecret()
    {
        var cut = Render<SecretDetailPage>(parameters => parameters.Add(page => page.SecretId, _firstId));
        cut.WaitForAssertion(() => Assert.Contains("First secret", cut.Find("#secret-heading").TextContent, StringComparison.Ordinal));

        cut.Render(parameters => parameters.Add(page => page.SecretId, _secondId));

        cut.WaitForAssertion(() => Assert.Contains("Second secret", cut.Find("#secret-heading").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void EditPage_RouteChangesToAnotherSecret_ShowsTheNewSecret()
    {
        var cut = Render<SecretEditPage>(parameters => parameters.Add(page => page.SecretId, _firstId));
        cut.WaitForAssertion(() => Assert.Equal("First secret", cut.Find("#secret-name-input").GetAttribute("value")));

        cut.Render(parameters => parameters.Add(page => page.SecretId, _secondId));

        cut.WaitForAssertion(() => Assert.Equal("Second secret", cut.Find("#secret-name-input").GetAttribute("value")));
    }

    [Fact]
    public void HistoryPage_RouteChangesToAnotherSecret_ShowsTheNewSecret()
    {
        var cut = Render<SecretHistoryPage>(parameters => parameters.Add(page => page.SecretId, _firstId));
        cut.WaitForAssertion(() => Assert.Contains("First secret", cut.Find("#history-heading").TextContent, StringComparison.Ordinal));

        cut.Render(parameters => parameters.Add(page => page.SecretId, _secondId));

        cut.WaitForAssertion(() => Assert.Contains("Second secret", cut.Find("#history-heading").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void NewPage_QueryChangesToAnotherFolder_PreselectsTheNewFolder()
    {
        var folders = new[]
        {
            new FolderDto { Id = Guid.NewGuid(), Name = "Servers" },
            new FolderDto { Id = Guid.NewGuid(), Name = "Banks" },
        };
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto { Items = [.. folders] })));
        var navigator = Services.GetRequiredService<NavigationManager>();
        navigator.NavigateTo($"/vault/secrets/new?folderId={folders[0].Id:D}");
        var cut = Render<SecretNewPage>();
        cut.WaitForAssertion(() => Assert.Equal("Servers", SelectedFolder(cut)));

        navigator.NavigateTo($"/vault/secrets/new?folderId={folders[1].Id:D}");

        cut.WaitForAssertion(() => Assert.Equal("Banks", SelectedFolder(cut)));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000x")]
    [InlineData("%00")]
    public void NewPage_FolderIdNotAGuid_RendersWithNoPreselectedFolder(string folderId)
    {
        var folder = new FolderDto { Id = Guid.NewGuid(), Name = "Servers" };
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto { Items = [folder] })));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/vault/secrets/new?folderId={folderId}");

        var cut = Render<SecretNewPage>();

        cut.WaitForElement("#secret-save");
        Assert.Equal("No folder", cut.Find("#secret-folder-select").QuerySelector("option[selected]")?.TextContent);
        Assert.Equal(VaultRoutes.Vault, cut.Find("#secret-cancel").GetAttribute("href"));
    }
}
