// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Pages.Sharing;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.UiShared.Sharing;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkSharedSecretDetailViewTests : SharedDetailTestHost
{
    private const string LinkButtonId = "#otl-new-link";

    [Theory]
    [InlineData(SharePermissionDto.View)]
    [InlineData(SharePermissionDto.Edit)]
    public void Permission_ViewOrEdit_HasNoLinkButton(SharePermissionDto permission)
    {
        ReturnsShared(permission);
        ReturnsLinksEnabled();

        var cut = RenderWithUser<SharedSecretPage>(Owner, parameters => parameters
            .Add(page => page.OwnerId, OwnerId)
            .Add(page => page.SecretId, SecretId));

        cut.WaitForElement("#shared-secret-heading");
        Assert.Empty(cut.FindAll(LinkButtonId));
    }

    [Fact]
    public void Permission_Manage_HasTheLinkButton()
    {
        ReturnsShared(SharePermissionDto.Manage);
        ReturnsLinksEnabled();

        var cut = RenderWithUser<SharedSecretPage>(Owner, parameters => parameters
            .Add(page => page.OwnerId, OwnerId)
            .Add(page => page.SecretId, SecretId));

        cut.WaitForElement(LinkButtonId);
    }

    private void ReturnsLinksEnabled() =>
        OneTimeLinksClient.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkSettingsDto>.Success(new OneTimeLinkSettingsDto { Enabled = true })));
}
