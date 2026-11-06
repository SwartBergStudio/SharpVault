// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Sharing;

public abstract class SharingTestHost : VaultTestContext
{
    protected SharingTestHost()
    {
        Provider = RenderDialogProviders();
        Owner = new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = "Owner One" };
    }

    protected IRenderedComponent<MudDialogProvider> Provider { get; }

    protected CurrentUserDto Owner { get; }

    protected static ShareGrantDto Grant(Guid recipientId, string name, SharePermissionDto permission = SharePermissionDto.View, bool canChange = true) => new()
    {
        RecipientId = recipientId,
        RecipientDisplayName = name,
        RecipientEmail = name.Replace(' ', '.').ToLowerInvariant() + "@example.test",
        Permission = permission,
        GrantedByDisplayName = "Owner One",
        GrantedAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
        ModifiedAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
        ConcurrencyToken = "etag-" + recipientId.ToString("N"),
        CanChange = canChange,
    };

    protected static Task<ApiResponse<ShareGrantListDto>> Grants(params ShareGrantDto[] grants) =>
        Task.FromResult(ApiResponse<ShareGrantListDto>.Success(new ShareGrantListDto { Items = [.. grants] }));

    protected IRenderedComponent<CascadingValue<CurrentUserDto?>> RenderWithUser<TComponent>(CurrentUserDto? user, Action<ComponentParameterCollectionBuilder<TComponent>> parameters)
        where TComponent : IComponent =>
        Render<CascadingValue<CurrentUserDto?>>(builder => builder
            .Add(cascade => cascade.Name, LayoutCascades.CurrentUser)
            .Add(cascade => cascade.Value, user)
            .AddChildContent<TComponent>(parameters));
}
