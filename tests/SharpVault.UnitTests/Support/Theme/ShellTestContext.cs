// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Idle;
using SharpVault.UI.Shared.Services.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Support.Theme;

public class ShellTestContext : MudTestContext
{
    public const string DisplayName = "Ada Lovelace";

    private readonly CapturingLoggerProvider _logs = new();

    public ShellTestContext()
    {
        Services.AddLogging(builder => builder.AddProvider(_logs));
        IdleOptions = new IdleLockOptions();
        SignInOptions = new SignInPresentationOptions();
        RoutedAssemblyList = [];
        Accessor = Substitute.For<ICurrentUserAccessor>();
        IdleMonitor = Substitute.For<IIdleMonitor>();
        MeClient = Substitute.For<IMeApiClient>();
        UsersClient = Substitute.For<IAdminUsersApiClient>();
        TransferNoticesClient = Substitute.For<ITransferNoticesApiClient>();
        TransferNoticesClient.ListAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ApiResponse<TransferNoticeListDto>.Success(new TransferNoticeListDto())));
        OneTimeLinksClient = Substitute.For<IOneTimeLinksApiClient>();
        OneTimeLinksClient.GetSettingsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ApiResponse<OneTimeLinkSettingsDto>.Success(new OneTimeLinkSettingsDto { DefaultLifetimeMinutes = 1440, MaxLifetimeMinutes = 10080 })));
        OneTimeLinksClient.ListNoticesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ApiResponse<OneTimeLinkNoticeListDto>.Success(new OneTimeLinkNoticeListDto())));
        Services.AddSingleton(Accessor);
        Services.AddSingleton(IdleMonitor);
        Services.AddSingleton(MeClient);
        Services.AddSingleton(UsersClient);
        Services.AddSingleton(TransferNoticesClient);
        Services.AddSingleton(OneTimeLinksClient);
        Services.AddSingleton(Options.Create(IdleOptions));
        Services.AddSingleton(Options.Create(SignInOptions));
        Services.AddSingleton(new RoutedAssemblies(RoutedAssemblyList));
        ReturnsCurrentUser(new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = DisplayName, IsAdmin = false });
    }

    public IdleLockOptions IdleOptions { get; }

    public SignInPresentationOptions SignInOptions { get; }

    public List<Assembly> RoutedAssemblyList { get; }

    public IReadOnlyList<string> LogMessages => _logs.Messages;

    public string LogText => _logs.AllText;

    public ICurrentUserAccessor Accessor { get; }

    public IIdleMonitor IdleMonitor { get; }

    public IMeApiClient MeClient { get; }

    public IAdminUsersApiClient UsersClient { get; }

    public ITransferNoticesApiClient TransferNoticesClient { get; }

    public IOneTimeLinksApiClient OneTimeLinksClient { get; }

    public void ReturnsCurrentUser(CurrentUserDto user) =>
        Accessor.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(ApiResponse<CurrentUserDto>.Success(user)));

    public void ReturnsCurrentUserStatus(ApiResponse<CurrentUserDto> response) =>
        Accessor.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(response));

    public void SignInAs(string displayName, bool isAdmin)
    {
        Authorization.SetAuthorized(displayName);
        ReturnsCurrentUser(new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = displayName, IsAdmin = isAdmin });
    }
}
