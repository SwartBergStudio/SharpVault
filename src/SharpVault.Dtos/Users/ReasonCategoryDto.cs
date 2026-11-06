// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json.Serialization;
using SharpVault.Dtos.Serialization;

namespace SharpVault.Dtos.Users;

[JsonConverter(typeof(StrictEnumJsonConverter<ReasonCategoryDto>))]
public enum ReasonCategoryDto
{
    LeftCompany = 1,
    Dismissed = 2,
    Deceased = 3,
    RoleChange = 4,
    Other = 5,
}
