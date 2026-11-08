// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.OneTimeLinks;

internal static class OneTimeLinkStorageMessages
{
    public const string PayloadTooLarge = "The one-time link payload is larger than the storage columns can hold.";

    public const string InvalidPayloadColumns = "A one-time link row has no payload column.";

    public const string InvalidKey = "A one-time link row has an invalid key.";
}
