// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SharpVault.IntegrationTests.Authentication;

public sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string ObjectIdHeader = "X-Test-Oid";
    public const string NameHeader = "X-Test-Name";
    public const string TenantHeader = "X-Test-Tid";
    public const string RolesHeader = "X-Test-Roles";

    private const string NameClaimType = "name";
    private const string RoleClaimType = "roles";
    private const string ObjectIdClaimType = "oid";
    private const string TenantIdClaimType = "tid";

    public static void AddHeaders(HttpClient client, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(user);

        client.DefaultRequestHeaders.Remove(ObjectIdHeader);
        client.DefaultRequestHeaders.Remove(NameHeader);
        client.DefaultRequestHeaders.Remove(TenantHeader);
        client.DefaultRequestHeaders.Remove(RolesHeader);
        client.DefaultRequestHeaders.Add(ObjectIdHeader, user.ObjectId.ToString());
        client.DefaultRequestHeaders.Add(NameHeader, user.DisplayName);
        client.DefaultRequestHeaders.Add(TenantHeader, user.TenantId.ToString());
        client.DefaultRequestHeaders.Add(RolesHeader, string.Join(',', user.Roles));
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ObjectIdHeader, out var objectId) || string.IsNullOrWhiteSpace(objectId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(SchemeName, NameClaimType, RoleClaimType);
        identity.AddClaim(new Claim(ObjectIdClaimType, objectId.ToString()));
        identity.AddClaim(new Claim(TenantIdClaimType, Request.Headers[TenantHeader].ToString()));
        identity.AddClaim(new Claim(NameClaimType, Request.Headers[NameHeader].ToString()));

        var roles = Request.Headers[RolesHeader].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(RoleClaimType, role));
        }

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
