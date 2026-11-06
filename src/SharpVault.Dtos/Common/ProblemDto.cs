// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Common;

public sealed class ProblemDto
{
    public string? Detail { get; set; }

    public Dictionary<string, string[]>? Errors { get; set; }
}
