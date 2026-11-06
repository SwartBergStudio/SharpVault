// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Transfers.Common;

public sealed class TransferOptions
{
    public const string SectionName = "Transfers";

    public bool Enabled { get; set; } = true;

    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(15);
}
