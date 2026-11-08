// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.OneTimeLinks;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkButtonTests : ShellTestContext
{
    private const string ButtonId = "#otl-new-link";

    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _secret = Guid.NewGuid();

    [Fact]
    public void Disabled_RendersNothing()
    {
        ReturnsSettings(ApiResponse<OneTimeLinkSettingsDto>.Success(new OneTimeLinkSettingsDto { Enabled = false }));

        var cut = Render<OneTimeLinkButton>(parameters => parameters.Add(button => button.OwnerId, _owner).Add(button => button.SecretId, _secret));

        cut.WaitForAssertion(() => OneTimeLinksClient.Received(1).GetSettingsAsync(Arg.Any<CancellationToken>()));
        Assert.Empty(cut.FindAll(ButtonId));
    }

    [Fact]
    public void Enabled_LinksToTheCreateRoute()
    {
        ReturnsSettings(ApiResponse<OneTimeLinkSettingsDto>.Success(new OneTimeLinkSettingsDto { Enabled = true }));

        var cut = Render<OneTimeLinkButton>(parameters => parameters.Add(button => button.OwnerId, _owner).Add(button => button.SecretId, _secret));

        Assert.Equal(OneTimeLinkRoutes.New(_owner, _secret), cut.WaitForElement(ButtonId).GetAttribute("href"));
    }

    [Fact]
    public void SettingsFail_RendersNothing()
    {
        ReturnsSettings(ApiResponse<OneTimeLinkSettingsDto>.Failed());

        var cut = Render<OneTimeLinkButton>(parameters => parameters.Add(button => button.OwnerId, _owner).Add(button => button.SecretId, _secret));

        cut.WaitForAssertion(() => OneTimeLinksClient.Received(1).GetSettingsAsync(Arg.Any<CancellationToken>()));
        Assert.Empty(cut.FindAll(ButtonId));
    }

    private void ReturnsSettings(ApiResponse<OneTimeLinkSettingsDto> response) =>
        OneTimeLinksClient.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(response));
}
