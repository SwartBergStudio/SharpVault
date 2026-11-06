// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Vault.Common;

public sealed class VaultRead : IDisposable
{
    private VaultRead(VaultReadState state, OpenedVault? vault)
    {
        State = state;
        Vault = vault;
    }

    public static VaultRead NoRing { get; } = new(VaultReadState.NoRing, null);

    public static VaultRead Unreadable { get; } = new(VaultReadState.Unreadable, null);

    public VaultReadState State { get; }

    public OpenedVault? Vault { get; }

    public static VaultRead Opened(OpenedVault vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return new VaultRead(VaultReadState.Opened, vault);
    }

    public void Dispose() => Vault?.Dispose();
}
