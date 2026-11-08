// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Navigation;

public static class OneTimeLinkRoutes
{
    public const string Links = "/one-time-links";

    public static string New(Guid ownerId, Guid secretId) => $"{Links}/new/{ownerId:D}/{secretId:D}";
}
