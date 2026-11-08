// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Dtos;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
public sealed partial class ApiRoutesTests(ApiTestFactory factory)
{
    private static readonly Dictionary<string, string[]> ExpectedVerbs = new()
    {
        [nameof(ApiRoutes.Me)] = ["GET"],
        [nameof(ApiRoutes.AdminUserCounts)] = ["GET"],
        [nameof(ApiRoutes.MeSessionCheck)] = ["POST"],
        [nameof(ApiRoutes.MeSessionRevoke)] = ["POST"],
        [nameof(ApiRoutes.AdminUsers)] = ["GET"],
        [nameof(ApiRoutes.AdminUserLock)] = ["POST"],
        [nameof(ApiRoutes.AdminUserUnlock)] = ["POST"],
        [nameof(ApiRoutes.AdminTransferPreview)] = ["GET"],
        [nameof(ApiRoutes.AdminTransfers)] = ["GET", "POST"],
        [nameof(ApiRoutes.AdminTransfer)] = ["GET"],
        [nameof(ApiRoutes.AdminTransferApprove)] = ["POST"],
        [nameof(ApiRoutes.AdminTransferReject)] = ["POST"],
        [nameof(ApiRoutes.AdminTransferAbort)] = ["POST"],
        [nameof(ApiRoutes.TransferNotices)] = ["GET"],
        [nameof(ApiRoutes.TransferNotice)] = ["DELETE"],
        [nameof(ApiRoutes.TransferOwnedShares)] = ["GET"],
        [nameof(ApiRoutes.VaultSecrets)] = ["GET", "POST"],
        [nameof(ApiRoutes.VaultSecret)] = ["DELETE", "GET", "PUT"],
        [nameof(ApiRoutes.VaultSecretMove)] = ["POST"],
        [nameof(ApiRoutes.VaultSecretReveal)] = ["POST"],
        [nameof(ApiRoutes.VaultSecretHistory)] = ["GET"],
        [nameof(ApiRoutes.VaultSecretVersionReveal)] = ["POST"],
        [nameof(ApiRoutes.VaultSecretVersionRestore)] = ["POST"],
        [nameof(ApiRoutes.VaultFolders)] = ["GET", "POST"],
        [nameof(ApiRoutes.VaultFolder)] = ["DELETE", "PUT"],
        [nameof(ApiRoutes.VaultFolderMove)] = ["POST"],
        [nameof(ApiRoutes.RecycleBin)] = ["GET"],
        [nameof(ApiRoutes.RecycleBinSecretRestore)] = ["POST"],
        [nameof(ApiRoutes.RecycleBinFolderRestore)] = ["POST"],
        [nameof(ApiRoutes.RecycleBinSecret)] = ["DELETE"],
        [nameof(ApiRoutes.RecycleBinFolder)] = ["DELETE"],
        [nameof(ApiRoutes.SharedItems)] = ["GET"],
        [nameof(ApiRoutes.SharedSecret)] = ["GET", "PUT"],
        [nameof(ApiRoutes.SharedSecretReveal)] = ["POST"],
        [nameof(ApiRoutes.SharedSecretFiling)] = ["PUT"],
        [nameof(ApiRoutes.SharedFolder)] = ["GET"],
        [nameof(ApiRoutes.SharedFolderFiling)] = ["PUT"],
        [nameof(ApiRoutes.SecretGrants)] = ["GET", "POST"],
        [nameof(ApiRoutes.SecretGrant)] = ["DELETE", "PUT"],
        [nameof(ApiRoutes.FolderGrants)] = ["GET", "POST"],
        [nameof(ApiRoutes.FolderGrant)] = ["DELETE", "PUT"],
        [nameof(ApiRoutes.DirectoryUsers)] = ["GET"],
        [nameof(ApiRoutes.OneTimeLinkSettings)] = ["GET"],
        [nameof(ApiRoutes.OneTimeLinkSecretSnapshot)] = ["POST"],
        [nameof(ApiRoutes.OneTimeLinkSecret)] = ["POST"],
        [nameof(ApiRoutes.OneTimeLinks)] = ["GET"],
        [nameof(ApiRoutes.OneTimeLink)] = ["DELETE"],
        [nameof(ApiRoutes.OneTimeLinkNotices)] = ["GET"],
        [nameof(ApiRoutes.OneTimeLinkNotice)] = ["DELETE"],
        [nameof(ApiRoutes.OneTimeLinkOpen)] = ["POST"],
    };

    private static IReadOnlyList<(string Name, string Value)> AllRoutes() =>
        [.. typeof(ApiRoutes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, (string)field.GetRawConstantValue()!))];

    private static readonly string[] StringKeyedRoutes = [nameof(ApiRoutes.OneTimeLinkNotice)];

    [GeneratedRegex(@"\{[A-Za-z]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex UnconstrainedPlaceholder();

    [Fact]
    public void Constants_Always_MatchTheMappedRouteTemplates()
    {
        var mapped = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText?.TrimEnd('/'))
            .ToList();

        foreach (var (name, value) in AllRoutes())
        {
            Assert.True(mapped.Contains(value), $"{name} is not mapped.");
        }
    }

    [Fact]
    public void Constants_Always_AreRootedAndWithoutTrailingSlash()
    {
        var routes = AllRoutes();

        Assert.NotEmpty(routes);
        foreach (var (name, value) in routes)
        {
            Assert.True(value.StartsWith('/'), $"{name} is not rooted.");
            Assert.False(value.EndsWith('/'), $"{name} has a trailing slash.");
        }
    }

    [Fact]
    public void Constants_EveryRoute_HasAnExpectedVerbEntryAndNoneIsMissing()
    {
        Assert.Equal(AllRoutes().Select(route => route.Name).Order(StringComparer.Ordinal), ExpectedVerbs.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Constants_EveryRoute_IsMappedWithExactlyTheDocumentedVerbs()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        foreach (var (name, value) in AllRoutes())
        {
            var verbs = endpoints
                .Where(endpoint => endpoint.RoutePattern.RawText?.TrimEnd('/') == value)
                .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            Assert.True(ExpectedVerbs[name].SequenceEqual(verbs), $"{name} is mapped for [{string.Join(", ", verbs)}].");
        }
    }

    [Fact]
    public void Constants_VaultRoutes_ConstrainEveryIdToGuid()
    {
        foreach (var (name, value) in AllRoutes().Where(route => !StringKeyedRoutes.Contains(route.Name)))
        {
            Assert.False(UnconstrainedPlaceholder().IsMatch(value), $"{name} has an unconstrained placeholder.");
        }
    }
}
