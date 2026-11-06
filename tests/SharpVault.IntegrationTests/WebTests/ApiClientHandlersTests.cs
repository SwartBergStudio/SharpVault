// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.IntegrationTests.WebTests;

public sealed class ApiClientHandlersTests
{
    [Fact]
    public void UsePrimaryHandler_CoversEveryApiClientInterface()
    {
        var interfaces = typeof(IMeApiClient).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == typeof(IMeApiClient).Namespace && type.Name.EndsWith("ApiClient", StringComparison.Ordinal))
            .ToList();
        var services = new ServiceCollection();

        services.UsePrimaryHandler(() => new CountingApiHandler());

        Assert.NotEmpty(interfaces);
        var uncovered = interfaces.Where(type => services.All(descriptor => descriptor.ServiceType != type)).Select(type => type.Name).ToList();
        Assert.Empty(uncovered);
    }
}
