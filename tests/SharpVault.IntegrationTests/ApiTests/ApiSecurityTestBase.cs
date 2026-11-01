// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests;

public abstract partial class ApiSecurityTestBase(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    protected const string MePath = "/api/me";
    protected const string AdminUsersPath = "/api/admin/users";
    protected const string UnknownPath = "/does-not-exist";
    protected const string ProblemJsonMediaType = "application/problem+json";
    private const int MaximumWriteAttempts = 50;
    private const int PreconditionFailed = 412;

    protected static HttpClient CreateClientFor(WebApplicationFactory<Program> source, string? token = null)
    {
        var client = source.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    protected async Task<HttpResponseMessage> GetWithTokenAsync(string path, string token)
    {
        using var client = Factory.CreateClientWithToken(token);
        return await client.GetAsync(path, TestContext.Current.CancellationToken);
    }

    protected WebApplicationFactory<Program> CreateTimedFactory(
        FakeTimeProvider time,
        TimeSpan cacheTtl,
        Action<IServiceCollection>? configure = null) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("UserAccess:CacheTtl", cacheTtl.ToString("c"));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
                configure?.Invoke(services);
            });
        });

    protected IReadOnlyList<RecordedStorageRequest> UserAndAuditRequests() =>
        [.. StorageRequests.Requests.Where(request => UserOrAuditPath().IsMatch(request.Path))];

    protected IReadOnlyList<RecordedStorageRequest> UserTableRequests() =>
        [.. StorageRequests.Requests.Where(request => UserTablePath().IsMatch(request.Path))];

    protected IReadOnlyList<RecordedStorageRequest> WriteRequests() =>
        [.. StorageRequests.Requests.Where(request => request.Method != "GET")];

    protected async Task<TableEntity> RequireUserRowAsync(Guid oid)
    {
        var row = await UserRows.GetAsync(GetUsersTable(), oid, TestContext.Current.CancellationToken);
        return Assert.IsType<TableEntity>(row);
    }

    protected async Task<IReadOnlyList<TableEntity>> ListAuditRowsAsync() =>
        await AuditRows.ListAsync(GetAuditTable(), TestContext.Current.CancellationToken);

    protected async Task SetUserStatusAsync(Guid oid, string status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            var row = await RequireUserRowAsync(oid);
            row[UserColumns.Status] = status;
            try
            {
                await GetUsersTable().UpdateEntityAsync(row, row.ETag, TableUpdateMode.Replace, cancellationToken);
                return;
            }
            catch (Azure.RequestFailedException exception) when (exception.Status == PreconditionFailed)
            {
            }
        }

        throw new InvalidOperationException("The user row kept changing while the status was written.");
    }

    protected Task LockUserAsync(Guid oid) => SetUserStatusAsync(oid, UserRows.LockedStatus);

    protected async Task InsertRawUserRowAsync(Guid oid, IDictionary<string, object?> columns)
    {
        var entity = new TableEntity(UserRows.PartitionKey, oid.ToString("D"));
        foreach (var column in columns)
        {
            entity[column.Key] = column.Value;
        }

        await GetUsersTable().AddEntityAsync(entity, TestContext.Current.CancellationToken);
    }

    protected static void AssertUnauthorizedOrForbiddenBodyIsClean(string body, params string[] forbidden)
    {
        foreach (var value in forbidden)
        {
            Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    protected static string Escape(string value) => Uri.EscapeDataString(value);

    [GeneratedRegex(@"^/[^/]+/(Users|AuditLog)(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex UserOrAuditPath();

    [GeneratedRegex(@"^/[^/]+/Users(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex UserTablePath();
}
