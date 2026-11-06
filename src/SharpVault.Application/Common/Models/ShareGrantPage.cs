// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Sharing;

namespace SharpVault.Application.Common.Models;

public sealed record ShareGrantPage(IReadOnlyList<Versioned<ShareGrant>> Items, string? ContinuationToken);
