// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedFiledPanelTests : SharingTestHost
{
    private readonly Guid _folderId = Guid.NewGuid();
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _secretId = Guid.NewGuid();
    private readonly Guid _ownSecretId = Guid.NewGuid();

    public SharedFiledPanelTests()
    {
        SecretsClient
            .ListSecretsAsync(Arg.Any<Guid?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SecretPageDto>.Success(new SecretPageDto
            {
                Items = [new SecretDto { Id = _ownSecretId, Name = "Own login", Type = SecretTypeDto.Login }],
            })));
    }

    private SharedItemDto Item(string name) => new()
    {
        OwnerId = _ownerId,
        OwnerDisplayName = "Ada Lovelace",
        ItemId = _secretId,
        Kind = SharedItemKindDto.Secret,
        Name = name,
        Type = SecretTypeDto.Login,
        Permission = SharePermissionDto.Edit,
        FiledFolderId = _folderId,
    };

    private void Returns(Task<ApiResponse<SharedItemPageDto>> response) =>
        SharedClient.ListAsync(_folderId, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ => response);

    [Fact]
    public void Empty_RendersNothingAtAll()
    {
        Returns(Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto())));

        var cut = Render<SharedFiledPanel>(parameters => parameters.Add(panel => panel.FolderId, _folderId));

        cut.WaitForAssertion(() => SharedClient.Received(1).ListAsync(_folderId, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => Assert.True(string.IsNullOrWhiteSpace(cut.Markup), cut.Markup));
    }

    [Fact]
    public void WithItems_ShowsTheNameOwnerAndAccessWithTheSharedLink()
    {
        Returns(Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto { Items = [Item("Payroll login")] })));

        var cut = Render<SharedFiledPanel>(parameters => parameters.Add(panel => panel.FolderId, _folderId));

        cut.WaitForElement("#shared-filed-panel");
        var link = cut.Find($"#shared-filed-link-secret-{_secretId:D}");
        Assert.Equal("Payroll login", link.TextContent.Trim());
        Assert.Equal(SharingRoutes.Secret(_ownerId, _secretId), link.GetAttribute("href"));
        Assert.Contains("Ada Lovelace", cut.Find("#shared-filed-panel").TextContent, StringComparison.Ordinal);
        Assert.Contains(PermissionLabels.Label(SharePermissionDto.Edit), cut.Find("#shared-filed-panel").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fails_ShowsRetryAndKeepsTheVaultListVisible()
    {
        Returns(Task.FromResult(ApiResponse<SharedItemPageDto>.Failed()));

        var cut = Render<VaultPage>(parameters => parameters.Add(page => page.FolderId, _folderId));

        cut.WaitForElement("#shared-filed-error");
        Assert.NotNull(cut.Find("#shared-filed-retry"));
        cut.WaitForAssertion(() => Assert.Contains("Own login", cut.Markup, StringComparison.Ordinal));
        Returns(Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto { Items = [Item("Payroll login")] })));

        await cut.Find("#shared-filed-retry").ClickAsync();

        cut.WaitForElement($"#shared-filed-link-secret-{_secretId:D}");
        Assert.Empty(cut.FindAll("#shared-filed-error"));
        Assert.Contains("Own login", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Fails_NotFound_ShowsTheAccessEndedTextOrTheGenericMissingText()
    {
        Returns(Task.FromResult(ApiResponse<SharedItemPageDto>.NotFound()));

        var cut = Render<SharedFiledPanel>(parameters => parameters.Add(panel => panel.FolderId, _folderId));

        cut.WaitForElement("#shared-filed-error");
        Assert.False(string.IsNullOrWhiteSpace(cut.Find("#shared-filed-error").TextContent));
    }

    [Fact]
    public void FolderChange_ReloadsForTheNewFolder()
    {
        var other = Guid.NewGuid();
        Returns(Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto { Items = [Item("Payroll login")] })));
        SharedClient.ListAsync(other, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto())));
        var cut = Render<VaultPage>(parameters => parameters.Add(page => page.FolderId, _folderId));
        cut.WaitForElement("#shared-filed-panel");

        cut.Render(parameters => parameters.Add(page => page.FolderId, other));

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#shared-filed-panel")));
        _ = SharedClient.Received().ListAsync(other, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Search_Active_DoesNotShowTheSharedPanelOrCallTheApi()
    {
        Returns(Task.FromResult(ApiResponse<SharedItemPageDto>.Success(new SharedItemPageDto { Items = [Item("Payroll login")] })));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo(VaultRoutes.Folder(_folderId) + "?" + VaultRoutes.SearchParameter + "=pay");

        var cut = Render<VaultPage>(parameters => parameters.Add(page => page.FolderId, _folderId));

        cut.WaitForAssertion(() => Assert.Contains("Own login", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#shared-filed-panel"));
        _ = SharedClient.DidNotReceive().ListAsync(_folderId, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}
