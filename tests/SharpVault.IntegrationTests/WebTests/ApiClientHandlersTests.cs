// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services.Clients;
using WebApp::SharpVault.Web.Api;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
public sealed class ApiClientHandlersTests(WebTestFactory factory)
{
    [Fact]
    [Trait("Category", "Security")]
    public void AddSharpVaultApiClient_EveryApiClientInterface_SendsAProof()
    {
        var handlers = factory.Services.GetRequiredService<IHttpMessageHandlerFactory>();

        var uncovered = ApiClientInterfaces()
            .Where(type => !HandlerChain(handlers.CreateHandler(type.Name)).Contains(typeof(WebGatewayProofHandler)))
            .Select(type => type.Name)
            .ToList();

        Assert.Empty(uncovered);
    }

    private static IEnumerable<Type> ApiClientInterfaces() =>
        typeof(IMeApiClient).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == typeof(IMeApiClient).Namespace && type.Name.EndsWith("ApiClient", StringComparison.Ordinal));

    private static List<Type> HandlerChain(HttpMessageHandler handler)
    {
        var types = new List<Type>();
        for (HttpMessageHandler? current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            types.Add(current.GetType());
        }

        return types;
    }

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
