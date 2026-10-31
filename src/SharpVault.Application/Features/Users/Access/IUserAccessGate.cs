// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Users.Access;

public interface IUserAccessGate
{
    Task<UserAccessDecision> CheckAsync(SignedInUser user, CancellationToken cancellationToken);
}
