// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Api.RateLimiting;

public static class RateLimitPolicies
{
    public const string Vault = "vault";

    public const string Reveal = "vault-reveal";

    public const string Sharing = "vault-sharing";

    public const string DirectorySearch = "directory-search";

    public const string AdminActions = "admin-actions";
}
