// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Encryption;

namespace SharpVault.UnitTests.Support.Encryption;

internal static class EncryptionContexts
{
    public const string DefaultPurpose = "secret.value";

    public static EncryptionContext Random(string purpose = DefaultPurpose) =>
        new(purpose, Guid.NewGuid(), Guid.NewGuid());
}
