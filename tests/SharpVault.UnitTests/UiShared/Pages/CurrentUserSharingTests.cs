// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Idle;
using SharpVault.UI.Shared.Services.Users;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Pages;

public sealed class CurrentUserSharingTests : MudTestContext
{
    private const string Name = "Ada Lovelace";

    private readonly IMeApiClient _meClient = Substitute.For<IMeApiClient>();

    public CurrentUserSharingTests()
    {
        Services.AddSingleton(_meClient);
        Services.AddSingleton(Substitute.For<IAdminUsersApiClient>());
        Services.AddSingleton(Substitute.For<IIdleMonitor>());
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new IdleLockOptions()));
        Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        SetRendererInfo(new RendererInfo("Server", true));
        Authorization.SetAuthorized(Name);
    }

    [Fact]
    public void AccountPage_Loaded_CallsTheApiExactlyOnceIncludingTheLayout()
    {
        _meClient.GetCurrentUserAsync(Arg.Any<CancellationToken>())
            .Returns(ApiResponse<CurrentUserDto>.Success(new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = Name, IsAdmin = false }));

        var layout = Render<MainLayout>(parameters => parameters.Add(l => l.Body, AccountBody()));

        layout.WaitForAssertion(() => Assert.Equal(Name, layout.Find("#account-display-name").TextContent));
        _ = _meClient.Received(1).GetCurrentUserAsync(Arg.Any<CancellationToken>());
    }

    private static RenderFragment AccountBody() =>
        builder =>
        {
            builder.OpenComponent<Account>(0);
            builder.CloseComponent();
        };
}
