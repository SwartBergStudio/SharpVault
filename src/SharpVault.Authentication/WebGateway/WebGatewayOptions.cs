// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Authentication.WebGateway;

public sealed class WebGatewayOptions
{
    public const string SectionName = "WebGateway";
    public const int MinimumKeyLength = 32;

    public string? Key { get; set; }

    public string? NormalizedKey => string.IsNullOrWhiteSpace(Key) ? null : Key.Trim();
}
