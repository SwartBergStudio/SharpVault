// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages.Transfers;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Transfers;

public abstract class TransferUiTestHost : ShellTestContext
{
    protected static readonly DateTimeOffset At = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    protected TransferUiTestHost()
    {
        CallerId = Guid.NewGuid();
        Transfers = Substitute.For<IAdminTransfersApiClient>();
        Grants = Substitute.For<IShareGrantsApiClient>();
        Services.AddSingleton(Transfers);
        Services.AddSingleton(Grants);
        SignInAsAdmin();
    }

    protected Guid CallerId { get; }

    protected IAdminTransfersApiClient Transfers { get; }

    protected IShareGrantsApiClient Grants { get; }

    protected void SignInAsAdmin() => ReturnsCurrentUser(new CurrentUserDto { UserId = CallerId, DisplayName = "Admin One", IsAdmin = true });

    protected void SignInAsNonAdmin() => ReturnsCurrentUser(new CurrentUserDto { UserId = CallerId, DisplayName = "User One", IsAdmin = false });

    protected static TransferSummaryDto Summary(Guid id, TransferStateDto state = TransferStateDto.PendingApproval) => new()
    {
        Id = id,
        State = state,
        SourceDisplayName = "User One",
        TargetDisplayName = "User Two",
        Category = ReasonCategoryDto.RoleChange,
        RequestedAt = At,
        ExpiresAt = At.AddDays(7),
        RequestedByName = "Admin One",
    };

    protected static TransferDto Transfer(
        Guid id,
        Guid requestedBy,
        TransferStateDto state = TransferStateDto.PendingApproval,
        bool canApprove = false,
        bool canReject = true,
        bool canAbort = false,
        string concurrencyToken = "etag-1") => new()
    {
        Id = id,
        State = state,
        SourceUserId = Guid.NewGuid(),
        SourceDisplayName = "User One",
        TargetUserId = Guid.NewGuid(),
        TargetDisplayName = "User Two",
        Scope = TransferScopeKindDto.AllItems,
        Category = ReasonCategoryDto.RoleChange,
        Reason = "Role change, the vault moves to the new owner",
        RequestedBy = requestedBy,
        RequestedByName = "Admin One",
        RequestedAt = At,
        ExpiresAt = At.AddDays(7),
        CanApprove = canApprove,
        CanReject = canReject,
        CanAbort = canAbort,
        ConcurrencyToken = concurrencyToken,
    };

    protected static TransferPreviewDto Preview(bool secondAdminAvailable = true, params TransferPreviewFolderDto[] folders) => new()
    {
        SourceUserId = Guid.NewGuid(),
        SourceDisplayName = "User One",
        SourceStatus = UserStatusDto.Active,
        ItemCount = 4,
        RecycledItemCount = 1,
        FolderCount = folders.Length,
        VersionCount = 2,
        TopLevelFolders = [.. folders],
        SecondAdminAvailable = secondAdminAvailable,
        HasOpenTransfer = false,
    };

    protected static TransferPreviewFolderDto Folder(Guid folderId, string reference, int items = 3) => new()
    {
        FolderId = folderId,
        Reference = reference,
        CreatedAt = At,
        ItemCount = items,
    };

    protected static UserSummaryDto User(Guid id, string name, UserStatusDto status = UserStatusDto.Active) => new()
    {
        UserId = id,
        DisplayName = name,
        Email = $"{name.ToLowerInvariant().Replace(' ', '.')}@sharpvault.invalid",
        Status = status,
        LastSignInAt = At,
    };

    protected IRenderedComponent<Router> RenderRouter(string path)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(path);
        return Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(AdminTransfersPage).Assembly)
            .Add(router => router.Found, routeData => builder =>
            {
                builder.OpenComponent(0, typeof(RouteView));
                builder.AddAttribute(1, nameof(RouteView.RouteData), routeData);
                builder.CloseComponent();
            })
            .Add(router => router.NotFoundPage, typeof(TransferRouteNotFound)));
    }

    protected static string Id(Guid value) => value.ToString("D");
}

[Route("/route-not-found-probe")]
public sealed class TransferRouteNotFound : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", "route-not-found");
        builder.CloseElement();
    }
}
