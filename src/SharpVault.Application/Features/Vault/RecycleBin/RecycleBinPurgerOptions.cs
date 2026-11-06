// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Vault.RecycleBin;

public sealed class RecycleBinPurgerOptions
{
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);
}
