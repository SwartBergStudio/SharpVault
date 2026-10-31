// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using SharpVault.Application.Common.Encryption;

namespace SharpVault.Infrastructure.Encryption;

public static class EnvelopeAssociatedData
{
    public const int MaxPurposeLength = 64;
    private const int LengthPrefixSize = 2;
    private const int GuidSize = 16;
    public const int MaxLength = EnvelopeFormat.VersionSize + LengthPrefixSize + MaxPurposeLength + (2 * (LengthPrefixSize + GuidSize));

    private static readonly SearchValues<char> PurposeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789.-");

    public static void EnsureValid(EncryptionContext context, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(context, parameterName);

        var purpose = context.Purpose;
        var purposeValid = purpose is { Length: >= 1 and <= MaxPurposeLength }
            && purpose[0] is >= 'a' and <= 'z'
            && !purpose.AsSpan().ContainsAnyExcept(PurposeCharacters);

        if (!purposeValid)
        {
            throw new ArgumentException(EncryptionMessages.InvalidPurpose, parameterName);
        }

        if (context.ItemId == Guid.Empty || context.OwnerId == Guid.Empty)
        {
            throw new ArgumentException(EncryptionMessages.EmptyContextId, parameterName);
        }
    }

    public static int Write(byte version, EncryptionContext context, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(context);

        var offset = 0;
        destination[offset++] = version;

        BinaryPrimitives.WriteUInt16BigEndian(destination[offset..], (ushort)context.Purpose.Length);
        offset += LengthPrefixSize;
        offset += Encoding.ASCII.GetBytes(context.Purpose, destination[offset..]);

        offset += WriteGuid(context.ItemId, destination[offset..]);
        offset += WriteGuid(context.OwnerId, destination[offset..]);

        return offset;
    }

    private static int WriteGuid(Guid value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, GuidSize);
        value.TryWriteBytes(destination[LengthPrefixSize..], bigEndian: true, out _);
        return LengthPrefixSize + GuidSize;
    }
}
