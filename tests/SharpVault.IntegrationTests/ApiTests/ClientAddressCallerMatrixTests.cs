// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class ClientAddressCallerMatrixTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private const string MeRoute = "/api/me";
    private const string AdminUsersRoute = "/api/admin/users";

    public static TheoryData<CallerKind, int, int> CallerRows() => new()
    {
        { CallerKind.Anonymous, (int)HttpStatusCode.Unauthorized, (int)HttpStatusCode.Unauthorized },
        { CallerKind.OtherTenantUser, (int)HttpStatusCode.Unauthorized, (int)HttpStatusCode.Unauthorized },
        { CallerKind.Guest, (int)HttpStatusCode.Forbidden, (int)HttpStatusCode.Forbidden },
        { CallerKind.LockedUser, (int)HttpStatusCode.Forbidden, (int)HttpStatusCode.Forbidden },
        { CallerKind.AuthenticatedWithoutRole, (int)HttpStatusCode.Forbidden, (int)HttpStatusCode.Forbidden },
        { CallerKind.User, (int)HttpStatusCode.OK, (int)HttpStatusCode.Forbidden },
        { CallerKind.Admin, (int)HttpStatusCode.OK, (int)HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(CallerRows))]
    public async Task ValidProofMatrix_EveryCaller_ReturnsTheSameStatusAsWithoutProof(CallerKind caller, int expectedMe, int expectedAdminUsers)
    {
        using var client = await ArrangeCallerAsync(caller);
        var userId = CallerClients.UserFor(caller)?.ObjectId ?? TestUsers.User.ObjectId;

        var meWithout = await StatusAsync(client, MeRoute, proof: null, userId);
        var meWith = await StatusAsync(client, MeRoute, ProofShape.Valid, userId);
        var adminWithout = await StatusAsync(client, AdminUsersRoute, proof: null, userId);
        var adminWith = await StatusAsync(client, AdminUsersRoute, ProofShape.Valid, userId);

        Assert.Equal((HttpStatusCode)expectedMe, meWithout);
        Assert.Equal(meWithout, meWith);
        Assert.Equal((HttpStatusCode)expectedAdminUsers, adminWithout);
        Assert.Equal(adminWithout, adminWith);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string route, ProofShape? proof, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (proof is { } shape)
        {
            ClientAddressProofs.Apply(request, shape, userId);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}
