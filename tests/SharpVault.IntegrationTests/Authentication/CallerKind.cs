// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Authentication;

public enum CallerKind
{
    Anonymous,
    AuthenticatedWithoutRole,
    AlgNoneToken,
    UnknownKeyToken,
    OtherTenantUser,
    Guest,
    User,
    Admin,
    LockedUser,
    LockedAdmin,
}
