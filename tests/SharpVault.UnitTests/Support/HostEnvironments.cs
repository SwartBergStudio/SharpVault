// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace SharpVault.UnitTests.Support;

internal static class HostEnvironments
{
    public static IHostEnvironment Named(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return environment;
    }
}
