// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.OneTimeLinks;

public interface IOneTimeLinkCrypto
{
    Task<OneTimeLinkSeal> SealAsync(string snapshotJson, string passphraseInputId, CancellationToken cancellationToken);

    Task PublishAsync(string handle, Guid linkId, string baseUri, string outputInputId, CancellationToken cancellationToken);

    Task<bool> CopyLinkAsync(string outputInputId, CancellationToken cancellationToken);

    Task DiscardAsync(string handle, CancellationToken cancellationToken);
}
