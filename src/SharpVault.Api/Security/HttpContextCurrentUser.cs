// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Access;
using SharpVault.ServiceDefaults.Identity;

namespace SharpVault.Api.Security;

public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor, EntraApplicationSettings entra) : ICurrentUser
{
    private const string NoUserMessage = "No signed-in user is available.";

    private SignedInIdentity? _identity;

    public Guid UserId => Identity.UserId;

    public string DisplayName => UserProfileText.NormalizeDisplayName(Identity.DisplayName);

    public bool IsAdmin => Identity.IsAdmin;

    private SignedInIdentity Identity => _identity ??= Read();

    private SignedInIdentity Read()
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true
            || !SignedInIdentityReader.TryRead(principal, entra, out var identity))
        {
            throw new InvalidOperationException(NoUserMessage);
        }

        return identity;
    }
}
