// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SharpVault.IntegrationTests.Authentication;

public static class CallerClients
{
    public static HttpClient CreateFor(WebApplicationFactory<Program> factory, CallerKind caller)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        var user = UserFor(caller);
        if (user is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(user));
            return client;
        }

        switch (caller)
        {
            case CallerKind.AlgNoneToken:
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ForgedTokens.AlgNone());
                break;
            case CallerKind.UnknownKeyToken:
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ForgedTokens.SignedWithUnknownKey());
                break;
            case CallerKind.Anonymous:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caller), caller, null);
        }

        return client;
    }

    public static TestUser? UserFor(CallerKind caller) => caller switch
    {
        CallerKind.AuthenticatedWithoutRole => TestUsers.AuthenticatedWithoutRole,
        CallerKind.OtherTenantUser => TestUsers.OtherTenantUser,
        CallerKind.Guest => TestUsers.Guest,
        CallerKind.User => TestUsers.User,
        CallerKind.Admin => TestUsers.Admin,
        CallerKind.LockedUser => TestUsers.LockedUser,
        CallerKind.LockedAdmin => TestUsers.LockedAdmin,
        _ => null,
    };
}
