// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Dtos.Transfers;

public sealed class TransferPageDto
{
    public List<TransferSummaryDto> Items { get; set; } = [];

    public string? ContinuationToken { get; set; }
}
