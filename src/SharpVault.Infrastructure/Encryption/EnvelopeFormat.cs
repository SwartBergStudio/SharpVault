// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Encryption;

public static class EnvelopeFormat
{
    public const byte Version1 = 0x01;
    public const int VersionSize = 1;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int HeaderSize = VersionSize + NonceSize;
    public const int Overhead = HeaderSize + TagSize;
    public const int DataKeySize = 32;
    public const int MaxPlaintextBytes = 32_768;
    public const int MaxEnvelopeBytes = MaxPlaintextBytes + Overhead;
}
