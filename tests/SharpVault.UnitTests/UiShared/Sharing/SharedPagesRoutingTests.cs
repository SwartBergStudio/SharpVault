// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Pages.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedPagesRoutingTests : SharedDetailTestHost
{
    private const string NotFoundMarker = "route-not-found";

    private readonly Guid _folderId = Guid.NewGuid();

    public SharedPagesRoutingTests()
    {
        ReturnsShared(SharePermissionDto.Edit);
        SharedItemsClient.GetFolderAsync(OwnerId, _folderId, Arg.Any<int>(), null, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(ApiResponse<SharedFolderContentsDto>.Success(new SharedFolderContentsDto
            {
                FolderId = _folderId,
                Name = "Servers",
                OwnerId = OwnerId,
                OwnerDisplayName = "Ada Lovelace",
                Permission = SharePermissionDto.View,
            })));
    }

    private IRenderedComponent<Router> RenderRouter(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(SharedSecretPage).Assembly)
            .Add(router => router.Found, routeData => builder =>
            {
                builder.OpenComponent(0, typeof(RouteView));
                builder.AddAttribute(1, nameof(RouteView.RouteData), routeData);
                builder.CloseComponent();
            })
            .Add(router => router.NotFoundPage, typeof(NotFoundProbe)));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/shared/not-a-guid/secrets/{id}")]
    [InlineData("/shared/{id}/secrets/not-a-guid")]
    [InlineData("/shared/{id}/secrets/not-a-guid/edit")]
    [InlineData("/shared/not-a-guid/folders/{id}")]
    [InlineData("/shared/{id}/folders/not-a-guid")]
    [InlineData("/shared/{id}/secrets/{id}%2F..")]
    [InlineData("/shared/{id}/secrets/{id}/edit/extra")]
    public void MalformedOrExtraSegments_DoNotMatchAnySharedPage(string template)
    {
        var path = template.Replace("{id}", Guid.NewGuid().ToString("D"), StringComparison.Ordinal);

        var cut = RenderRouter(path);

        Assert.NotNull(cut.Find("#" + NotFoundMarker));
        _ = SharedItemsClient.DidNotReceiveWithAnyArgs().GetSecretAsync(default, default, Xunit.TestContext.Current.CancellationToken);
        _ = SharedItemsClient.DidNotReceiveWithAnyArgs().GetFolderAsync(default, default, default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void WellFormedSecretRoute_RendersTheDetailPageForThoseIds()
    {
        var cut = RenderRouter(SharingRoutes.Secret(OwnerId, SecretId));

        cut.WaitForElement("#shared-secret-heading");
        Assert.Empty(cut.FindAll("#" + NotFoundMarker));
    }

    [Fact]
    public void WellFormedEditRoute_RendersTheEditorForThoseIds()
    {
        var cut = RenderRouter(SharingRoutes.EditSecret(OwnerId, SecretId));

        cut.WaitForElement("#shared-secret-editor-heading");
        Assert.Empty(cut.FindAll("#" + NotFoundMarker));
    }

    [Fact]
    public void WellFormedFolderRoute_RendersTheFolderPageForThoseIds()
    {
        var cut = RenderRouter(SharingRoutes.Folder(OwnerId, _folderId));

        cut.WaitForElement("#shared-folder-heading");
        Assert.Empty(cut.FindAll("#" + NotFoundMarker));
    }

    [Fact]
    public void SharedListRoute_RendersTheSharedWithMePage()
    {
        var cut = RenderRouter(SharingRoutes.Shared);

        cut.WaitForElement("#shared-heading");
        Assert.IsAssignableFrom<IComponent>(cut.FindComponent<SharedWithMePage>().Instance);
    }

    [Route("/route-not-found-probe")]
    private sealed class NotFoundProbe : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            builder.AddMarkupContent(0, $"<p id=\"{NotFoundMarker}\">missing</p>");
    }
}
