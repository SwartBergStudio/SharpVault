// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Api.Security;
using SharpVault.Dtos;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferEndpointMetadataTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const int ExpectedTransferEndpoints = 12;

    private static readonly string[] AdminRoutes =
    [
        ApiRoutes.AdminTransferPreview,
        ApiRoutes.AdminTransfers,
        ApiRoutes.AdminTransfer,
        ApiRoutes.AdminTransferApprove,
        ApiRoutes.AdminTransferReject,
        ApiRoutes.AdminTransferAbort,
        ApiRoutes.AdminUserLock,
        ApiRoutes.AdminUserUnlock,
    ];

    private static readonly string[] UserRoutes =
    [
        ApiRoutes.TransferNotices,
        ApiRoutes.TransferNotice,
        ApiRoutes.TransferOwnedShares,
    ];

    private List<RouteEndpoint> TransferEndpoints()
    {
        _ = Factory.Server;
        return [.. Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => AdminRoutes.Contains(endpoint.RoutePattern.RawText) || UserRoutes.Contains(endpoint.RoutePattern.RawText))];
    }

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join(',', endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])} {endpoint.RoutePattern.RawText}";

    private static IReadOnlyList<string> PoliciesOf(RouteEndpoint endpoint) =>
        [.. endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void TransferEndpoints_AllHaveExplicitPolicyMetadata()
    {
        var missing = TransferEndpoints()
            .Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().All(data => string.IsNullOrEmpty(data.Policy)))
            .Select(Describe)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void TransferEndpoints_AllAreMapped()
    {
        Assert.Equal(ExpectedTransferEndpoints, TransferEndpoints().Count);
    }

    [Fact]
    public void TransferEndpoints_AdminRoutes_CarryTheAdminPolicyOnly()
    {
        var offenders = TransferEndpoints()
            .Where(endpoint => AdminRoutes.Contains(endpoint.RoutePattern.RawText))
            .Where(endpoint => !PoliciesOf(endpoint).SequenceEqual([Policies.Admin]))
            .Select(endpoint => $"{Describe(endpoint)} => {string.Join('|', PoliciesOf(endpoint))}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TransferEndpoints_UserRoutes_CarryTheUserPolicyOnly()
    {
        var offenders = TransferEndpoints()
            .Where(endpoint => UserRoutes.Contains(endpoint.RoutePattern.RawText))
            .Where(endpoint => !PoliciesOf(endpoint).SequenceEqual([Policies.User]))
            .Select(endpoint => $"{Describe(endpoint)} => {string.Join('|', PoliciesOf(endpoint))}")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TransferEndpoints_None_AllowAnonymous()
    {
        var offenders = TransferEndpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .ToList();

        Assert.Empty(offenders);
    }
}
