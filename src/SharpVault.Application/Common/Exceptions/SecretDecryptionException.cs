// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Exceptions;

public sealed class SecretDecryptionException : Exception
{
    public const string DefaultMessage = "The encrypted value could not be decrypted.";

    public SecretDecryptionException(DecryptionFailureReason reason)
        : base(DefaultMessage)
    {
        Reason = reason;
    }

    public DecryptionFailureReason Reason { get; }
}
