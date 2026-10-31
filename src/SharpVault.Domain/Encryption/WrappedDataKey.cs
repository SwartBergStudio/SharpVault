// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Domain.Encryption;

public sealed record WrappedDataKey(string KeyId, string Algorithm, byte[] Value)
{
    public override string ToString() => $"WrappedDataKey {{ KeyId = {KeyId}, Algorithm = {Algorithm}, Length = {Value?.Length ?? 0} }}";
}
