// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Storage;

public static class TableNames
{
    public const string Users = "Users";

    public const string SessionRevocations = "SessionRevocations";

    public const string OneTimeLinks = "OneTimeLinks";

    public const string AuditLog = "AuditLog";

    public const string Vaults = "Vaults";

    public const string SecretAuditLog = "SecretAuditLog";

    public const string Maintenance = "Maintenance";

    public const string ShareInbox = "ShareInbox";

    public const string TransferJobs = "TransferJobs";

    public static IReadOnlyList<string> All { get; } = [Users, AuditLog, Vaults, SecretAuditLog, Maintenance, ShareInbox, TransferJobs, SessionRevocations, OneTimeLinks];
}
