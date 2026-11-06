// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using Bunit;
using NSubstitute;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class SecretDetailViewDegradedTests : VaultTestContext
{
    private readonly Fixture _fixture = new();
    private readonly SecretDto _secret;

    public SecretDetailViewDegradedTests()
    {
        _secret = new SecretDto
        {
            Id = _fixture.Create<Guid>(),
            Type = SecretTypeDto.Login,
            Name = "name-" + _fixture.Create<Guid>().ToString("N"),
            Username = "user",
            Tags = ["alpha"],
            FolderId = _fixture.Create<Guid>(),
            HasValue = true,
            ConcurrencyToken = "etag-" + _fixture.Create<Guid>().ToString("N"),
        };
        SecretsClient.GetSecretAsync(_secret.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<SecretDto>.Success(_secret)));
    }

    [Theory]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.RateLimited)]
    [InlineData(ApiCallStatus.NotFound)]
    public void Render_FolderListFails_StillShowsTheItem(ApiCallStatus status)
    {
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ApiResponse<FolderListDto>(status, null)));

        var cut = Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#secret-heading")));
        Assert.Equal(_secret.Name, cut.Find("#secret-heading").TextContent.Trim());
        Assert.Empty(cut.FindAll("#secret-detail-error"));
        Assert.Equal(FolderNodes.DamagedName, cut.Find("#secret-folder").TextContent);
        Assert.NotNull(cut.Find("#secret-delete"));
    }

    [Fact]
    public void Render_FolderListFails_DisablesMoveAndSaysWhy()
    {
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ApiResponse<FolderListDto>(ApiCallStatus.Failed, null)));

        var cut = Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));

        cut.WaitForElement("#secret-heading");
        Assert.True(cut.Find("#secret-move").HasAttribute("disabled"));
        Assert.NotNull(cut.Find("#secret-folders-unavailable"));
    }

    [Theory]
    [InlineData(ApiCallStatus.SignInRequired)]
    [InlineData(ApiCallStatus.AccessDenied)]
    public void Render_FolderListSessionStatus_DoesNotShowTheItem(ApiCallStatus status)
    {
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ApiResponse<FolderListDto>(status, null)));

        var cut = Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));

        Assert.Empty(cut.FindAll("#secret-heading"));
    }

    [Fact]
    public void Damaged_ShowsDamagedPanelWithoutFields()
    {
        _secret.IsDamaged = true;
        _secret.Name = null;
        _secret.Username = null;
        _secret.Tags = [];
        FoldersClient.ListFoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<FolderListDto>.Success(new FolderListDto())));

        var cut = Render<SecretDetailView>(parameters => parameters.Add(view => view.SecretId, _secret.Id));

        cut.WaitForElement("#secret-damaged");
        Assert.Empty(cut.FindAll("#secret-fields"));
        Assert.Empty(cut.FindAll("#secret-value-reveal"));
    }
}
