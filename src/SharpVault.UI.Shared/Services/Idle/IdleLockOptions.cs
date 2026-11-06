// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.Idle;

public sealed class IdleLockOptions
{
    public const string SectionName = "IdleLock";
    public static readonly TimeSpan MinimumTimeout = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromHours(8);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    public TimeSpan Timeout { get; set; } = DefaultTimeout;
}
