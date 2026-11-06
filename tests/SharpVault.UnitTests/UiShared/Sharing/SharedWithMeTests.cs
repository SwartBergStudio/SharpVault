// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedWithMeTests : SharingTestHost
{
    private readonly Guid _adaId = Guid.NewGuid();
    private readonly Guid _bobId = Guid.NewGuid();
    private readonly Guid _secretId = Guid.NewGuid();
    private readonly Guid _folderItemId = Guid.NewGuid();
    private readonly Guid _financeId = Guid.NewGuid();
    private readonly Guid _goneFolderId = Guid.NewGuid();

    public SharedWithMeTests()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(SharingRoutes.Shared);
        SharedClient.ListAsync(null, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Page(
                Item(_bobId, "Bob Builder", _folderItemId, SharedItemKindDto.Folder, "Zeta folder", _goneFolderId),
                Item(_adaId, "Ada Lovelace", _secretId, SharedItemKindDto.Secret, "Payroll login", _financeId)));
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto
            {
                Items = [new FolderDto { Id = _financeId, Name = "Finance" }],
            })));
        SharedClient.FileSecretAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<FileSharedItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.Success(true)));
        SharedClient.FileFolderAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<FileSharedItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.Success(true)));
    }

    private static Task<ApiResponse<SharedItemPageDto>> Page(params SharedItemDto[] items) =>
        Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto { Items = [.. items] }));

    private static SharedItemDto Item(Guid ownerId, string ownerName, Guid itemId, SharedItemKindDto kind, string name, Guid? filed = null) => new()
    {
        OwnerId = ownerId,
        OwnerDisplayName = ownerName,
        ItemId = itemId,
        Kind = kind,
        Name = name,
        Type = kind == SharedItemKindDto.Secret ? SecretTypeDto.Login : null,
        Permission = SharePermissionDto.View,
        FiledFolderId = filed,
    };

    [Fact]
    public void SharedWithMePage_ListsGroupsSortedByOwnerWithLinksToTheDetailRoutes()
    {
        var cut = Render<SharedWithMePage>();

        cut.WaitForElement($"#shared-owner-{_adaId:D}");
        var headings = cut.FindAll("[id^=shared-owner-heading-]").Select(heading => heading.TextContent.Trim()).ToList();
        Assert.Equal(["From Ada Lovelace", "From Bob Builder"], headings);
        Assert.Equal(SharingRoutes.Secret(_adaId, _secretId), cut.Find($"#shared-item-link-secret-{_secretId:D}").GetAttribute("href"));
        Assert.Equal(SharingRoutes.Folder(_bobId, _folderItemId), cut.Find($"#shared-item-link-folder-{_folderItemId:D}").GetAttribute("href"));
    }

    [Fact]
    public void SharedWithMePage_NothingShared_ShowsAHint()
    {
        SharedClient.ListAsync(null, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ => Page());

        var cut = Render<SharedWithMePage>();

        cut.WaitForElement("#shared-items-empty");
        Assert.NotNull(cut.Find("#shared-items-empty-description"));
    }

    [Fact]
    public void SharedWithMePage_LoadFails_ShowsTheErrorWithRetry()
    {
        SharedClient.ListAsync(null, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedItemPageDto>.Failed()));

        var cut = Render<SharedWithMePage>();

        cut.WaitForElement("#shared-items-error");
        Assert.NotNull(cut.Find("#shared-items-retry"));
    }

    [Fact]
    public void SharedWithMePage_FiledFolderGone_ShowsTheItemAsUnfiledWithoutReadingPerRow()
    {
        var cut = Render<SharedWithMePage>();

        cut.WaitForElement($"#shared-item-filed-folder-{_folderItemId:D}");
        Assert.Equal(SharedItemText.NotFiled, cut.Find($"#shared-item-filed-folder-{_folderItemId:D}").TextContent);
        Assert.Equal("Finance", cut.Find($"#shared-item-filed-secret-{_secretId:D}").TextContent);
        FoldersClient.Received(1).ListFoldersAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SharedWithMePage_FolderListFailed_DoesNotClaimAnyItemIsUnfiled()
    {
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Failed()));

        var cut = Render<SharedWithMePage>();

        cut.WaitForElement($"#shared-item-filed-secret-{_secretId:D}");
        Assert.Equal(SharedItemText.FiledUnknown, cut.Find($"#shared-item-filed-secret-{_secretId:D}").TextContent);
    }

    [Fact]
    public void SharedWithMePage_FileSecret_SendsTheChosenFolderAndShowsItFiled()
    {
        var cut = Render<SharedWithMePage>();
        cut.WaitForElement($"#shared-item-file-folder-{_folderItemId:D}");

        cut.Find($"#shared-item-file-secret-{_secretId:D}").Click();
        Provider.WaitForElement("#file-shared-folder-select").Change(_financeId.ToString("D"));
        Provider.Find("#file-shared-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Filed, cut.Find("#shared-status").TextContent, StringComparison.Ordinal));
        SharedClient.Received(1).FileSecretAsync(_adaId, _secretId, Arg.Is<FileSharedItemRequestDto>(request => request.FolderId == _financeId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SharedWithMePage_FileFolderIntoNoFolder_UnfilesItAndSaysSo()
    {
        var cut = Render<SharedWithMePage>();
        cut.WaitForElement($"#shared-item-file-folder-{_folderItemId:D}");

        cut.Find($"#shared-item-file-folder-{_folderItemId:D}").Click();
        Provider.WaitForElement("#file-shared-folder-select");
        Provider.Find("#file-shared-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.Unfiled, cut.Find("#shared-status").TextContent, StringComparison.Ordinal));
        SharedClient.Received(1).FileFolderAsync(_bobId, _folderItemId, Arg.Is<FileSharedItemRequestDto>(request => request.FolderId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SharedWithMePage_FolderListFailsWhenDialogOpens_ShowsTheErrorAndKeepsTheRows()
    {
        var cut = Render<SharedWithMePage>();
        cut.WaitForElement($"#shared-item-file-secret-{_secretId:D}");
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<FolderListDto>.Failed()));

        cut.Find($"#shared-item-file-secret-{_secretId:D}").Click();

        Provider.WaitForElement("#file-shared-error");
        Assert.NotNull(cut.Find($"#shared-item-link-secret-{_secretId:D}"));
        Assert.NotNull(cut.Find($"#shared-item-link-folder-{_folderItemId:D}"));
    }

    [Fact]
    public void SharedWithMePage_FilingFails_ShowsTheErrorInTheDialog()
    {
        SharedClient.FileSecretAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<FileSharedItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.Failed()));
        var cut = Render<SharedWithMePage>();
        cut.WaitForElement($"#shared-item-file-secret-{_secretId:D}");

        cut.Find($"#shared-item-file-secret-{_secretId:D}").Click();
        Provider.WaitForElement("#file-shared-folder-select").Change(_financeId.ToString("D"));
        Provider.Find("#file-shared-confirm").Click();

        Provider.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.FilingFailed, Provider.Find("#file-shared-error").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void SharedWithMePage_AccessEndedWhileFiling_ShowsTheNoticeAndReloads()
    {
        SharedClient.FileSecretAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<FileSharedItemRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<bool>.NotFound()));
        var cut = Render<SharedWithMePage>();
        cut.WaitForElement($"#shared-item-file-secret-{_secretId:D}");

        cut.Find($"#shared-item-file-secret-{_secretId:D}").Click();
        Provider.WaitForElement("#file-shared-confirm").Click();

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-status").TextContent, StringComparison.Ordinal));
        SharedClient.Received(2).ListAsync(null, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void VaultPage_FolderSelected_ShowsItemsSharedAndFiledInThatFolder()
    {
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto())));
        SharedClient.ListAsync(_financeId, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Page(Item(_adaId, "Ada Lovelace", _secretId, SharedItemKindDto.Secret, "Payroll login", _financeId)));
        Services.GetRequiredService<NavigationManager>().NavigateTo(VaultRoutes.Folder(_financeId));

        var cut = Render<VaultPage>(parameters => parameters.Add(page => page.FolderId, _financeId));

        cut.WaitForElement($"#shared-filed-link-secret-{_secretId:D}");
        Assert.Equal(SharingRoutes.Secret(_adaId, _secretId), cut.Find($"#shared-filed-link-secret-{_secretId:D}").GetAttribute("href"));
    }

    [Fact]
    public void VaultPage_FolderWithNothingSharedIn_ShowsNoSharedPanel()
    {
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto())));

        var cut = Render<VaultPage>(parameters => parameters.Add(page => page.FolderId, _financeId));

        cut.WaitForAssertion(() => SharedClient.Received().ListAsync(_financeId, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()));
        Assert.Empty(cut.FindAll("#shared-filed-panel"));
        Assert.Empty(cut.FindAll("#shared-filed-error"));
    }
}
