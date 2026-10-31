// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.ServiceDefaults.Identity;

public enum TokenCheckResult
{
    Invalid = 0,
    Valid,
    WrongTenant,
    InvalidObjectId,
    AppOnlyToken,
    MissingScope,
}
