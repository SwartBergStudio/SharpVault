// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Infrastructure.Users;

namespace SharpVault.UnitTests.Support;

internal static class TypeAdapterRegistration
{
    private static readonly object Gate = new();

    public static void EnsureInfrastructureMappings()
    {
        lock (Gate)
        {
            TypeAdapterConfig.GlobalSettings.Scan(typeof(UserKeys).Assembly);
        }
    }
}
