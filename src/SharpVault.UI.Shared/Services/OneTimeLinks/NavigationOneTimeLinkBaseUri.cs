// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;

namespace SharpVault.UI.Shared.Services.OneTimeLinks;

public sealed class NavigationOneTimeLinkBaseUri(NavigationManager navigation) : IOneTimeLinkBaseUri
{
    public string Value => navigation.BaseUri;
}
