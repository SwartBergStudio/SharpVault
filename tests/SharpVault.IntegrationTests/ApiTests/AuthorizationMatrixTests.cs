// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// TEN-2, TEN-3, TEN-4 and LCK-1: every caller against every endpoint with the exact status.
/// </summary>
[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class AuthorizationMatrixTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private static readonly string[] AnonymousPaths = ["/health", "/alive", "/openapi/v1.json"];

    private static readonly CallerKind[] DeniedBeforeTheGate =
    [
        CallerKind.Anonymous,
        CallerKind.AlgNoneToken,
        CallerKind.UnknownKeyToken,
        CallerKind.OtherTenantUser,
        CallerKind.Guest,
        CallerKind.AuthenticatedWithoutRole,
    ];

    private static readonly CallerKind[] Locked = [CallerKind.LockedUser, CallerKind.LockedAdmin];

    private static readonly (CallerKind Caller, HttpStatusCode Me, HttpStatusCode AdminUsers, HttpStatusCode Unknown)[] ProtectedRows =
    [
        (CallerKind.Anonymous, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
        (CallerKind.AlgNoneToken, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
        (CallerKind.UnknownKeyToken, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
        (CallerKind.OtherTenantUser, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized),
        (CallerKind.Guest, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
        (CallerKind.AuthenticatedWithoutRole, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
        (CallerKind.User, HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.NotFound),
        (CallerKind.Admin, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.NotFound),
        (CallerKind.LockedUser, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
        (CallerKind.LockedAdmin, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden),
    ];

    public static TheoryData<string, string, CallerKind, HttpStatusCode> Matrix()
    {
        var data = new TheoryData<string, string, CallerKind, HttpStatusCode>();
        foreach (var row in ProtectedRows)
        {
            data.Add("GET", MePath, row.Caller, row.Me);
            data.Add("GET", AdminUsersPath, row.Caller, row.AdminUsers);
            data.Add("GET", UnknownPath, row.Caller, row.Unknown);
            foreach (var path in AnonymousPaths)
            {
                data.Add("GET", path, row.Caller, HttpStatusCode.OK);
            }
        }

        return data;
    }

    public static TheoryData<CallerKind, string> DeniedCallersAndPaths()
    {
        var data = new TheoryData<CallerKind, string>();
        foreach (var caller in DeniedBeforeTheGate)
        {
            foreach (var path in new[] { MePath, AdminUsersPath, UnknownPath })
            {
                data.Add(caller, path);
            }
        }

        return data;
    }

    public static TheoryData<CallerKind, string> LockedCallersAndPaths()
    {
        var data = new TheoryData<CallerKind, string>();
        foreach (var caller in Locked)
        {
            foreach (var path in new[] { MePath, AdminUsersPath, UnknownPath })
            {
                data.Add(caller, path);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task AuthorizationMatrix_EveryEndpointAndCaller_ReturnsExpectedStatus(
        string method,
        string path,
        CallerKind caller,
        HttpStatusCode expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await ArrangeCallerAsync(caller);
        StorageRequests.Clear();

        var response = await client.SendAsync(method, path, cancellationToken);

        Assert.Equal(expected, response.StatusCode);
        AssertStorageFootprint(caller, path, expected);
        AssertNoQueryWithoutPartitionKey();
    }

    [Theory]
    [MemberData(nameof(DeniedCallersAndPaths))]
    public async Task DeniedCallers_AnyEndpoint_MakeNoStorageCalls(CallerKind caller, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await ArrangeCallerAsync(caller);
        StorageRequests.Clear();

        var response = await client.GetAsync(path, cancellationToken);

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"Unexpected status {response.StatusCode}.");
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [MemberData(nameof(LockedCallersAndPaths))]
    public async Task LockedCallers_AnyEndpoint_ReadTheirOwnRowOnlyAndWriteNothing(CallerKind caller, string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await ArrangeCallerAsync(caller);
        var user = CallerClients.UserFor(caller)!;
        var before = await RequireUserRowAsync(user.ObjectId);
        StorageRequests.Clear();

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        if (ReachesTheGate(caller, path))
        {
            var read = Assert.Single(StorageRequests.Requests);
            Assert.Equal("GET", read.Method);
            Assert.Contains(user.ObjectId.ToString("D"), read.Path, StringComparison.Ordinal);
            Assert.False(read.IsEntityQuery);
        }
        else
        {
            Assert.Equal(0, StorageRequests.Count);
        }

        var after = await RequireUserRowAsync(user.ObjectId);
        Assert.Equal(before.ETag, after.ETag);
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task HealthEndpoint_ValidUserToken_MakesNoUsersTableCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);
        StorageRequests.Clear();

        var response = await Client.GetAsync("/alive", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(UserAndAuditRequests());
    }

    [Theory]
    [InlineData(CallerKind.User)]
    [InlineData(CallerKind.Admin)]
    [InlineData(CallerKind.LockedUser)]
    [InlineData(CallerKind.Guest)]
    [InlineData(CallerKind.Anonymous)]
    public async Task AnonymousEndpoints_AnyCaller_NeverTouchUsersOrAuditTables(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await ArrangeCallerAsync(caller);
        StorageRequests.Clear();

        foreach (var path in AnonymousPaths)
        {
            var response = await client.GetAsync(path, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Empty(UserAndAuditRequests());
    }

    private void AssertStorageFootprint(CallerKind caller, string path, HttpStatusCode status)
    {
        if (AnonymousPaths.Contains(path))
        {
            Assert.Empty(UserAndAuditRequests());
            return;
        }

        if (Locked.Contains(caller) && !ReachesTheGate(caller, path))
        {
            Assert.Equal(0, StorageRequests.Count);
            return;
        }

        if (Locked.Contains(caller))
        {
            var read = Assert.Single(StorageRequests.Requests);
            Assert.Equal("GET", read.Method);
            Assert.False(read.IsEntityQuery);
            Assert.Empty(WriteRequests());
            return;
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Assert.Equal(0, StorageRequests.Count);
        }
    }

    private static bool ReachesTheGate(CallerKind caller, string path) =>
        caller != CallerKind.LockedUser || path != AdminUsersPath;
}
