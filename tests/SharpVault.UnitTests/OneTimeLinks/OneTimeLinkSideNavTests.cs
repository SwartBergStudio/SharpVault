// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkSideNavTests : ShellTestContext
{
    [Fact]
    public void SignedIn_HasTheLinksEntry()
    {
        Authorization.SetAuthorized(DisplayName);
        var cut = Render<SideNav>(parameters =>
        {
            parameters.Add(nav => nav.Open, true);
            parameters.AddCascadingValue(LayoutCascades.CurrentUser, new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = DisplayName, IsAdmin = false });
        });

        Assert.Equal(OneTimeLinkRoutes.Links, cut.WaitForElement("#nav-one-time-links").GetAttribute("href"));
    }
}
