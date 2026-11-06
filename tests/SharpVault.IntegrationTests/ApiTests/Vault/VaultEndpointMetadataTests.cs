// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Api.Security;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultEndpointMetadataTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const string VaultPrefix = "/api/vault";
    private const int MinimumVaultEndpoints = 20;

    private List<RouteEndpoint> VaultEndpoints()
    {
        _ = Factory.Server;
        return [.. Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(VaultPrefix, StringComparison.Ordinal) == true)];
    }

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join(',', endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])} {endpoint.RoutePattern.RawText}";

    [Fact]
    public void VaultEndpoints_Always_AreMoreThanTheDocumentedMinimum()
    {
        Assert.True(VaultEndpoints().Count >= MinimumVaultEndpoints);
    }

    [Fact]
    public void VaultEndpoints_Every_CarriesTheUserPolicyExplicitly()
    {
        var missing = VaultEndpoints()
            .Where(endpoint => !endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == Policies.User))
            .Select(Describe)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void VaultEndpoints_Every_IsNeitherAnonymousNorAdminPolicy()
    {
        var offenders = VaultEndpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == Policies.Admin))
            .Select(Describe)
            .ToList();

        Assert.Empty(offenders);
    }
}
