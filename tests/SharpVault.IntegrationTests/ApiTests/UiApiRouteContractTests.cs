// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Factories;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
public sealed partial class UiApiRouteContractTests(ApiTestFactory factory)
{
    private const string DtosAssemblyName = "SharpVault.Dtos";
    private const string ApiRoutesTypeName = "SharpVault.Dtos.ApiRoutes";
    private const string ApiQueryTypeName = "SharpVault.Dtos.ApiQuery";
    private const string SolutionFileName = "SharpVault.slnx";
    private const string ClientMarker = "Client";

    private static readonly string[] ClientFolders =
    [
        Path.Combine("src", "SharpVault.UI.Shared", "Services"),
    ];

    private static readonly Type[] ScalarQueryTypes =
    [
        typeof(string), typeof(int), typeof(long), typeof(bool), typeof(Guid), typeof(DateTimeOffset), typeof(DateTime),
    ];

    [Fact]
    [Trait("Category", "Security")]
    public void EveryApiRoutesConstant_IsMappedByTheApi()
    {
        var constants = RouteConstants();
        var mapped = MappedRoutes().Select(endpoint => Normalise(endpoint.RoutePattern.RawText)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unmapped = constants
            .Where(constant => !mapped.Contains(Normalise((string)constant.GetRawConstantValue()!)))
            .Select(constant => constant.Name)
            .ToList();

        Assert.NotEmpty(constants);
        Assert.Empty(unmapped);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void EveryUiClientCall_UsesAnApiRoutesConstantWithTheRightVerb()
    {
        var calls = ClientCalls();
        var endpoints = MappedRoutes();
        var routeValues = RouteConstants().ToDictionary(constant => constant.Name, constant => Normalise((string)constant.GetRawConstantValue()!));

        var violations = new List<string>();
        foreach (var call in calls)
        {
            if (!routeValues.TryGetValue(call.Route, out var path))
            {
                violations.Add($"{call.File}: ApiRoutes.{call.Route} does not exist");
                continue;
            }

            var accepted = endpoints
                .Where(endpoint => string.Equals(Normalise(endpoint.RoutePattern.RawText), path, StringComparison.OrdinalIgnoreCase))
                .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .ToList();
            if (!accepted.Contains(call.Verb, StringComparer.OrdinalIgnoreCase))
            {
                violations.Add($"{call.File}: {call.Verb} ApiRoutes.{call.Route} is not accepted (accepts {string.Join(",", accepted)})");
            }
        }

        Assert.NotEmpty(calls);
        Assert.Empty(violations);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void EveryUiClientCall_IncludesTheRequiredQueryParameters()
    {
        var calls = ClientCalls();
        var endpoints = MappedRoutes();
        var routeValues = RouteConstants().ToDictionary(constant => constant.Name, constant => Normalise((string)constant.GetRawConstantValue()!));
        var queryNames = QueryConstants();

        var violations = new List<string>();
        foreach (var call in calls.Where(call => routeValues.ContainsKey(call.Route)))
        {
            var path = routeValues[call.Route];
            var endpoint = endpoints.FirstOrDefault(candidate =>
                string.Equals(Normalise(candidate.RoutePattern.RawText), path, StringComparison.OrdinalIgnoreCase)
                && (candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(call.Verb, StringComparer.OrdinalIgnoreCase) ?? false));
            var handler = endpoint?.Metadata.GetMetadata<MethodInfo>();
            if (handler is null)
            {
                continue;
            }

            foreach (var parameter in RequiredQueryParameters(handler, endpoint!.RoutePattern))
            {
                var hasConstant = queryNames.Contains(parameter, StringComparer.OrdinalIgnoreCase);
                var referenced = call.Statement.Contains("ApiQuery." + ToPascal(parameter), StringComparison.Ordinal);
                if (!hasConstant || !referenced)
                {
                    violations.Add($"{call.File}: {call.Verb} ApiRoutes.{call.Route} lacks required query parameter '{parameter}'");
                }
            }
        }

        Assert.NotEmpty(calls);
        Assert.Empty(violations);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ClientCalls_FindAllTwentyVaultCalls()
    {
        var vaultClients = new[]
        {
            (File: "VaultSecretsApiClient.cs", Contract: typeof(IVaultSecretsApiClient)),
            (File: "SecretHistoryApiClient.cs", Contract: typeof(ISecretHistoryApiClient)),
            (File: "VaultFoldersApiClient.cs", Contract: typeof(IVaultFoldersApiClient)),
            (File: "RecycleBinApiClient.cs", Contract: typeof(IRecycleBinApiClient)),
        };
        var calls = ClientCalls();

        foreach (var client in vaultClients)
        {
            Assert.Equal(client.Contract.GetMethods().Length, calls.Count(call => call.File == client.File));
        }

        Assert.Equal(20, vaultClients.Sum(client => calls.Count(call => call.File == client.File)));
    }

    private static IEnumerable<string> RequiredQueryParameters(MethodInfo handler, RoutePattern pattern)
    {
        var routeParameters = pattern.Parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nullability = new NullabilityInfoContext();
        foreach (var parameter in handler.GetParameters())
        {
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            if (!ScalarQueryTypes.Contains(type) || routeParameters.Contains(parameter.Name!))
            {
                continue;
            }

            var isNullable = Nullable.GetUnderlyingType(parameter.ParameterType) is not null
                || nullability.Create(parameter).WriteState == NullabilityState.Nullable
                || parameter.HasDefaultValue;
            if (!isNullable)
            {
                yield return parameter.Name!;
            }
        }
    }

    private List<RouteEndpoint> MappedRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    private static List<FieldInfo> RouteConstants() => ConstantsOf(ApiRoutesTypeName);

    private static List<string> QueryConstants() =>
        ConstantsOf(ApiQueryTypeName).Select(constant => (string)constant.GetRawConstantValue()!).ToList();

    private static List<FieldInfo> ConstantsOf(string typeName)
    {
        var type = Assembly.Load(new AssemblyName(DtosAssemblyName)).GetType(typeName)
            ?? throw new InvalidOperationException(typeName + " was not found in " + DtosAssemblyName);
        return type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToList();
    }

    private static List<ClientCall> ClientCalls()
    {
        var root = LocateRepositoryRoot();
        var files = ClientFolders
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(path => Path.GetFileName(path).Contains(ClientMarker, StringComparison.Ordinal))
            .ToList();

        var calls = new List<ClientCall>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            calls.AddRange(HelperCall().Matches(text).Select(match => new ClientCall(
                name, VerbOf(match.Groups["verb"].Value), match.Groups["route"].Value, StatementFrom(text, match.Index))));
            calls.AddRange(RequestCall().Matches(text).Select(match => new ClientCall(
                name, match.Groups["verb"].Value.ToUpperInvariant(), match.Groups["route"].Value, StatementFrom(text, match.Index))));
        }

        return calls;
    }

    private static string StatementFrom(string text, int index)
    {
        var end = text.IndexOf(';', index);
        return end < 0 ? text[index..] : text[index..end];
    }

    private static string VerbOf(string helper) => helper switch
    {
        _ when helper.StartsWith("Get", StringComparison.Ordinal) => "GET",
        _ when helper.StartsWith("Post", StringComparison.Ordinal) => "POST",
        _ when helper.StartsWith("Put", StringComparison.Ordinal) => "PUT",
        _ => "DELETE",
    };

    private static string Normalise(string? route) => (route ?? string.Empty).Trim('/');

    private static string ToPascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be located.");
    }

    [GeneratedRegex(@"(?<verb>GetAsync|PostAsync|PutAsync|DeleteAsync|GetFromJsonAsync|PostAsJsonAsync|PutAsJsonAsync)\s*(?:<[^>]*>)?\s*\(\s*ApiRoutes\.(?<route>\w+)")]
    private static partial Regex HelperCall();

    [GeneratedRegex(@"new\s+HttpRequestMessage\s*\(\s*HttpMethod\.(?<verb>\w+)\s*,\s*ApiRoutes\.(?<route>\w+)")]
    private static partial Regex RequestCall();

    private sealed record ClientCall(string File, string Verb, string Route, string Statement);
}
