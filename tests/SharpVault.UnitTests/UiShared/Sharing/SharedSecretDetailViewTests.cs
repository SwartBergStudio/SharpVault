// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedSecretDetailViewTests : SharedDetailTestHost
{
    private const string RevealedValue = "s3cret-Value-9f2";

    public SharedSecretDetailViewTests()
    {
        ReturnsReveal(RevealedValue);
        GrantsClient.ListSecretGrantsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Grants(Grant(Guid.NewGuid(), "Grace Hopper")));
    }

    private IRenderedComponent<CascadingValue<SharpVault.Dtos.Users.CurrentUserDto?>> RenderPage() =>
        RenderWithUser<SharedSecretPage>(Owner, parameters => parameters
            .Add(page => page.OwnerId, OwnerId)
            .Add(page => page.SecretId, SecretId));

    [Fact]
    public void View_ShowsOwnerPermissionAndNoEditNoPanelNoFolderNoHistory()
    {
        ReturnsShared(SharePermissionDto.View);

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-heading");
        Assert.Equal("Router login", cut.Find("#shared-secret-heading").TextContent.Trim());
        Assert.Contains("Ada Lovelace", cut.Find("#shared-secret-owner").TextContent, StringComparison.Ordinal);
        Assert.Contains(PermissionLabels.Label(SharePermissionDto.View), cut.Find("#shared-secret-permission").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#shared-secret-edit"));
        Assert.Empty(cut.FindAll("#share-panel"));
        Assert.Empty(cut.FindAll("#secret-folder"));
        Assert.Empty(cut.FindAll("#secret-history-link"));
        Assert.Empty(cut.FindAll("#secret-delete"));
        GrantsClient.DidNotReceiveWithAnyArgs().ListSecretGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Edit_HasEditLinkToTheSharedRouteAndNoPanel()
    {
        ReturnsShared(SharePermissionDto.Edit);

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-edit");
        Assert.EndsWith(SharingRoutes.EditSecret(OwnerId, SecretId), cut.Find("#shared-secret-edit").GetAttribute("href"), StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#share-panel"));
        GrantsClient.DidNotReceiveWithAnyArgs().ListSecretGrantsAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Manage_ShowsPanelForTheSharedOwnerNotTheCaller()
    {
        ReturnsShared(SharePermissionDto.Manage);

        var cut = RenderPage();

        cut.WaitForElement("#share-add");
        GrantsClient.Received(1).ListSecretGrantsAsync(OwnerId, SecretId, Arg.Any<CancellationToken>());
        Assert.NotNull(cut.Find("#shared-secret-edit"));
        Assert.Empty(cut.FindAll("#secret-history-link"));
    }

    [Fact]
    public void Damaged_HasNoFieldsNoEditButManagerKeepsThePanel()
    {
        ReturnsShared(SharePermissionDto.Manage, damaged: true);

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-damaged");
        cut.WaitForElement("#share-add");
        Assert.Empty(cut.FindAll("#secret-fields"));
        Assert.Empty(cut.FindAll("#shared-secret-edit"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reveal_UsesTheSharedRouteShowsSensitiveValueAndHidesIt()
    {
        ReturnsShared(SharePermissionDto.View);
        var cut = RenderPage();
        cut.WaitForElement("#secret-value-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(RevealedValue, cut.Find("#secret-value").TextContent, StringComparison.Ordinal));
        Assert.Equal("true", cut.Find("#secret-value").GetAttribute("data-sv-sensitive"));
        _ = SharedItemsClient.Received(1).RevealSecretAsync(
            OwnerId,
            SecretId,
            Arg.Is<RevealSecretRequestDto>(request => request.Field == RevealFieldDto.Value && request.Intent == RevealIntentDto.View),
            Arg.Any<CancellationToken>());
        _ = SecretsClient.DidNotReceiveWithAnyArgs().RevealSecretAsync(default, default!, Xunit.TestContext.Current.CancellationToken);

        await cut.Find("#secret-value-hide").ClickAsync();
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Copy_UsesTheSharedRouteWithCopyIntentAndWritesTheClipboard()
    {
        ReturnsShared(SharePermissionDto.View);
        var cut = RenderPage();

        cut.WaitForElement("#secret-value-copy").Click();

        cut.WaitForAssertion(() => Assert.Equal([RevealedValue], Clipboard.Writes));
        SharedItemsClient.Received(1).RevealSecretAsync(
            OwnerId,
            SecretId,
            Arg.Is<RevealSecretRequestDto>(request => request.Intent == RevealIntentDto.Copy),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Load_NotFound_ShowsAccessEndedNotTheGenericMissingText()
    {
        SharedItemsClient.GetSecretAsync(OwnerId, SecretId, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedSecretDto>.NotFound()));

        var cut = RenderPage();

        cut.WaitForElement("#shared-secret-detail-error");
        Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-secret-detail-error").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Reveal_NotFoundAfterRevoke_ShowsAccessEndedInTheStatus()
    {
        ReturnsShared(SharePermissionDto.View);
        SharedItemsClient.RevealSecretAsync(OwnerId, SecretId, Arg.Any<RevealSecretRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<RevealedValueDto>.NotFound()));
        var cut = RenderPage();

        cut.WaitForElement("#secret-value-reveal").Click();

        cut.WaitForAssertion(() => Assert.Contains(SharedItemStatusText.AccessEnded, cut.Find("#shared-secret-status").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(RevealedValue, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_RouteChange_ReloadsForTheNewIds()
    {
        ReturnsShared(SharePermissionDto.View);
        var cut = RenderPage();
        cut.WaitForElement("#shared-secret-heading");
        var otherOwner = Guid.NewGuid();
        var otherSecret = Guid.NewGuid();
        SharedItemsClient.GetSecretAsync(otherOwner, otherSecret, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedSecretDto>.Success(new SharedSecretDto
            {
                OwnerId = otherOwner,
                OwnerDisplayName = "Other",
                Permission = SharePermissionDto.View,
                Item = new SecretDto { Id = otherSecret, Name = "Second", Type = SecretTypeDto.Login },
            })));

        cut.Render(builder => builder
            .Add(cascade => cascade.Name, LayoutCascades.CurrentUser)
            .Add(cascade => cascade.Value, Owner)
            .AddChildContent<SharedSecretPage>(parameters => parameters
                .Add(page => page.OwnerId, otherOwner)
                .Add(page => page.SecretId, otherSecret)));

        cut.WaitForAssertion(() => Assert.Equal("Second", cut.Find("#shared-secret-heading").TextContent.Trim()));
    }

    [Theory]
    [InlineData(typeof(SharedSecretPage))]
    [InlineData(typeof(SharedSecretEditPage))]
    [InlineData(typeof(SharedFolderPage))]
    public void Pages_RouteConstrainsEveryIdToAGuid(Type page)
    {
        var template = page.GetCustomAttribute<RouteAttribute>()!.Template;

        Assert.Contains("{OwnerId:guid}", template, StringComparison.Ordinal);
        Assert.DoesNotContain("{SecretId}", template, StringComparison.Ordinal);
        Assert.DoesNotContain("{FolderId}", template, StringComparison.Ordinal);
    }
}
