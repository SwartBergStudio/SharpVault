// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.IdentityModel.Tokens;

namespace SharpVault.Authentication.Local;

public sealed class LocalSigningKey
{
    private const string Redacted = "LocalSigningKey";

    public LocalSigningKey(byte[] material)
    {
        ArgumentNullException.ThrowIfNull(material);
        Key = new SymmetricSecurityKey(material) { KeyId = LocalAuthenticationDefaults.KeyId };
    }

    public SymmetricSecurityKey Key { get; }

    public override string ToString() => Redacted;
}
