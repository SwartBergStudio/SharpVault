// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Api.Security;
using SharpVault.Dtos;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingEndpointMetadataTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private static readonly string[] SharingRoutes =
    [
        ApiRoutes.SharedItems,
        ApiRoutes.SharedSecret,
        ApiRoutes.SharedSecretReveal,
        ApiRoutes.SharedSecretFiling,
        ApiRoutes.SharedFolder,
        ApiRoutes.SharedFolderFiling,
        ApiRoutes.SecretGrants,
        ApiRoutes.SecretGrant,
        ApiRoutes.FolderGrants,
        ApiRoutes.FolderGrant,
        ApiRoutes.DirectoryUsers,
    ];

    private const int ExpectedSharingEndpoints = 16;

    private List<RouteEndpoint> SharingEndpoints()
    {
        _ = Factory.Server;
        return [.. Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => SharingRoutes.Contains(endpoint.RoutePattern.RawText))];
    }

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join(',', endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])} {endpoint.RoutePattern.RawText}";

    [Fact]
    public void SharingEndpoints_Always_AreAllMapped()
    {
        Assert.Equal(ExpectedSharingEndpoints, SharingEndpoints().Count);
    }

    [Fact]
    public void SharingEndpoints_Every_CarriesTheUserPolicyExplicitly()
    {
        var missing = SharingEndpoints()
            .Where(endpoint => !endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == Policies.User))
            .Select(Describe)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void SharingEndpoints_Every_IsNeitherAnonymousNorAdminPolicy()
    {
        var offenders = SharingEndpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == Policies.Admin))
            .Select(Describe)
            .ToList();

        Assert.Empty(offenders);
    }
}
