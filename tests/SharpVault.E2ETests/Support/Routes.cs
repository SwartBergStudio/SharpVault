// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.E2ETests.Support;

public static class Routes
{
    public const string Home = "/";
    public const string LocalSignIn = "/account/local-sign-in";
    public const string Vault = "/vault";
    public const string Unfiled = "/vault/unfiled";
    public const string RecycleBin = "/vault/recycle-bin";
    public const string Shared = "/shared";
    public const string Account = "/account";
    public const string AdminUsers = "/admin/users";
    public const string AdminTransfers = "/admin/transfers";
    public const string NewTransfer = "/admin/transfers/new";
    public const string TransferShares = "/transfers/shares";
    public const string SharedSecretPrefix = "/shared/";
    public const string SecretsSegment = "/secrets/";
    public const string VaultSecretPrefix = "/vault/secrets/";
}
