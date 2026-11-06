// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Vault.RecycleBin;

namespace SharpVault.UnitTests.Support.Vault;

internal static class EraserFactory
{
    public static RecycledItemEraser For(IVaultStore store, IShareInboxStore? inbox = null) =>
        new(store, inbox ?? Substitute.For<IShareInboxStore>(), TimeProvider.System, NullLogger<RecycledItemEraser>.Instance);
}
