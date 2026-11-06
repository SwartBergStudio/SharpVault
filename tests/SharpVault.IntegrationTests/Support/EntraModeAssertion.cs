// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using SharpVault.Authentication.Mode;

namespace SharpVault.IntegrationTests.Support;

public static class EntraModeAssertion
{
    public static void Assert(IServiceProvider services) => Require(services, AuthenticationMode.Entra);

    public static void AssertLocal(IServiceProvider services) => Require(services, AuthenticationMode.Local);

    private static void Require(IServiceProvider services, AuthenticationMode expected)
    {
        ArgumentNullException.ThrowIfNull(services);

        var actual = services.GetRequiredService<AuthenticationModeInfo>().Mode;
        if (actual != expected)
        {
            throw new InvalidOperationException($"The test host runs in {actual} authentication mode but {expected} was expected.");
        }
    }
}
