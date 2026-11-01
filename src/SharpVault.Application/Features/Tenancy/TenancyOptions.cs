// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Tenancy;

public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    public bool AllowGuests { get; set; }
}
