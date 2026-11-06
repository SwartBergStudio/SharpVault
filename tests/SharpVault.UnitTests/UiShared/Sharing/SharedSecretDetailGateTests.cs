// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedSecretDetailGateTests : SharedDetailTestHost
{
    private const string RevealedValue = "gate-Value-31c";

    public SharedSecretDetailGateTests()
    {
        ReturnsReveal(RevealedValue);
        GrantsClient.ListSecretGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(Guid.NewGuid(), "Grace Hopper")));
    }

    private IRenderedComponent<CascadingValue<SharpVault.Dtos.Users.CurrentUserDto?>> RenderPage() =>
        RenderWithUser<SharedSecretPage>(Owner, parameters => parameters
            .Add(page => page.OwnerId, OwnerId)
            .Add(page => page.SecretId, SecretId));

    private void RevealReturns(ApiResponse<RevealedValueDto> response) =>
        SharedItemsClient.RevealSecretAsync(OwnerId, SecretId, Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(response));

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(SharePermissionDto.View, false, false)]
    [InlineData(SharePermissionDto.Edit, false, true)]
    [InlineData(SharePermissionDto.Manage, true, true)]
    [InlineData((SharePermissionDto)0, false, false)]
    [InlineData((SharePermissionDto)99, false, false)]
    public void Permission_DecidesSharePanelAndEditLink(SharePermissionDto permission, bool panel, bool edit)
    {
        ReturnsShared(permission);

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-heading");
        cut.WaitForAssertion(() => Assert.Equal(panel, cut.FindAll("#share-panel").Count == 1));
        Assert.Equal(edit, cut.FindAll("#shared-secret-edit").Count == 1);
        if (!panel)
        {
            _ = GrantsClient.DidNotReceiveWithAnyArgs().ListSecretGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_AccessEnded_ShowsNoValueAndTheAccessEndedStatus()
    {
        ReturnsShared(SharePermissionDto.View);
        RevealReturns(ApiResponse<RevealedValueDto>.NotFound());
        var cut = RenderPage();

        await cut.WaitForElement("#secret-value-reveal").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-secret-status").TextContent, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#secret-value-hide"));
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Copy_AccessEnded_WritesNothingToTheClipboardAndShowsTheAccessEndedStatus()
    {
        ReturnsShared(SharePermissionDto.View);
        RevealReturns(ApiResponse<RevealedValueDto>.NotFound());
        var cut = RenderPage();

        await cut.WaitForElement("#secret-value-copy").ClickAsync();

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-secret-status").TextContent, StringComparison.Ordinal));
        Assert.Empty(Clipboard.Writes);
    }

    [Fact]
    public async Task Reveal_SignInRequired_RedirectsToSignInAndShowsNoValue()
    {
        ReturnsShared(SharePermissionDto.View);
        RevealReturns(ApiResponse<RevealedValueDto>.SignInRequired());
        var cut = RenderPage();

        await cut.WaitForElement("#secret-value-reveal").ClickAsync();

        cut.WaitForAssertion(() =>
        {
            var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
            Assert.StartsWith(AccountRoutes.SignIn, navigation.History.First().Uri, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reveal_Failed_ShowsAGenericErrorWithoutTheValue()
    {
        ReturnsShared(SharePermissionDto.View);
        RevealReturns(ApiResponse<RevealedValueDto>.Failed());
        var cut = RenderPage();

        await cut.WaitForElement("#secret-value-reveal").ClickAsync();

        cut.WaitForAssertion(() => Assert.False(string.IsNullOrWhiteSpace(cut.Find("#shared-secret-status").TextContent)));
        Assert.DoesNotContain(SharedItemStatusText.AccessEnded, cut.Find("#shared-secret-status").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_SignInRequired_RedirectsToSignIn()
    {
        SharedItemsClient.GetSecretAsync(OwnerId, SecretId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedSecretDto>.SignInRequired()));

        var cut = RenderPage();

        var navigation = Assert.IsType<BunitNavigationManager>(Services.GetRequiredService<NavigationManager>());
        cut.WaitForAssertion(() => Assert.StartsWith(AccountRoutes.SignIn, navigation.History.First().Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void Load_Failed_ShowsTheErrorWithRetryAndNoFields()
    {
        SharedItemsClient.GetSecretAsync(OwnerId, SecretId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedSecretDto>.Failed()));

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-detail-error");
        Assert.NotNull(cut.Find("#shared-secret-detail-retry"));
        Assert.Empty(cut.FindAll("#secret-fields"));
    }
}
