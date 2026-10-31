// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.Identity.Web;
using NSubstitute;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Support;

public static class FakeTokenAcquisition
{
    private const string ObjectIdClaim = "oid";
    private const int PrincipalArgumentIndex = 4;

    public static ITokenAcquisition Create()
    {
        var acquisition = Substitute.For<ITokenAcquisition>();
        acquisition
            .GetAccessTokenForUserAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<TokenAcquisitionOptions?>())
            .Returns(call =>
            {
                var principal = call.ArgAt<ClaimsPrincipal?>(PrincipalArgumentIndex);
                var oid = Guid.Parse(principal?.FindFirst(ObjectIdClaim)?.Value ?? throw new InvalidOperationException("The principal has no object id."));
                return Task.FromResult(TestTokens.ForObjectId(oid));
            });
        return acquisition;
    }
}
