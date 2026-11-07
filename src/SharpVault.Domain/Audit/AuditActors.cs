// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Audit;

public static class AuditActors
{
    public static readonly Guid System = Guid.Empty;

    public static readonly Guid AnonymousLinkRecipient = new("00000000-0000-0000-0000-000000000001");
}
