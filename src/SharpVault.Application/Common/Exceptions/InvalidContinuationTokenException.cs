// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Exceptions;

public sealed class InvalidContinuationTokenException : Exception
{
    public const string DefaultMessage = "The continuation token was rejected by the store.";

    public InvalidContinuationTokenException()
        : base(DefaultMessage)
    {
    }

    public InvalidContinuationTokenException(Exception innerException)
        : base(DefaultMessage, innerException)
    {
    }
}
