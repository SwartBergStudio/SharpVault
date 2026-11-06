// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;

namespace SharpVault.UI.Shared.Components.Vault;

public static class SecretTypeLabels
{
    public static IReadOnlyList<SvSelectOption> Options { get; } =
    [
        .. Enum.GetValues<SecretTypeDto>().Select(type => new SvSelectOption(type.ToString(), Label(type))),
    ];

    public static string Label(SecretTypeDto type) => type switch
    {
        SecretTypeDto.Login => "Login",
        SecretTypeDto.SecureNote => "Secure note",
        SecretTypeDto.ApiKey => "API key",
        SecretTypeDto.Card => "Card",
        SecretTypeDto.Other => "Other",
        _ => "Unknown",
    };

    public static SecretTypeDto Parse(string? value, SecretTypeDto fallback) =>
        Enum.TryParse<SecretTypeDto>(value, ignoreCase: false, out var type) && Enum.GetName(type) == value ? type : fallback;
}
