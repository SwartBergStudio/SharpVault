// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Common;

namespace SharpVault.UI.Shared.Components.Users;

public static class ReasonCategoryLabels
{
    public static IReadOnlyList<SvSelectOption> Options { get; } = Enum.GetValues<ReasonCategoryDto>()
        .Select(category => new SvSelectOption(category.ToString(), Label(category)))
        .ToList();

    public static string Label(ReasonCategoryDto category) => category switch
    {
        ReasonCategoryDto.LeftCompany => "Left the company",
        ReasonCategoryDto.Dismissed => "Dismissed",
        ReasonCategoryDto.Deceased => "Deceased",
        ReasonCategoryDto.RoleChange => "Role change",
        ReasonCategoryDto.Other => "Other",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown reason category."),
    };

    public static bool TryParse(string? text, out ReasonCategoryDto category)
    {
        if (Enum.TryParse(text, out category) && Enum.IsDefined(category))
        {
            return true;
        }

        category = default;
        return false;
    }
}
