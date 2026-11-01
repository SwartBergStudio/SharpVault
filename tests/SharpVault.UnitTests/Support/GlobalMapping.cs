// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Application.Mapping;

namespace SharpVault.UnitTests.Support;

internal static class GlobalMapping
{
    private static readonly object Gate = new();

    public static void EnsureApplicationMappings()
    {
        lock (Gate)
        {
            TypeAdapterConfig.GlobalSettings.Scan(typeof(UserMappingRegister).Assembly);
        }
    }
}
