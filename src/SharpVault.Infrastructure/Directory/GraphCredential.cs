// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Core;

namespace SharpVault.Infrastructure.Directory;

public sealed record GraphCredential(TokenCredential Credential)
{
    public override string ToString() => nameof(GraphCredential);
}
