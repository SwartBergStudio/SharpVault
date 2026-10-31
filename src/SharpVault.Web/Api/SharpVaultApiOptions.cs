// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web.Api;

public sealed class SharpVaultApiOptions
{
    public const string SectionName = "SharpVaultApi";

    public Uri? BaseAddress { get; set; }

    public string[] Scopes { get; set; } = [];
}
