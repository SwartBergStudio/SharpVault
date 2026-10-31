// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Encryption;

namespace SharpVault.UnitTests.Support.Encryption;

internal sealed class EncryptionHarness
{
    public EncryptionHarness()
    {
        Encryptor = new EnvelopeEncryptor(Provider, Logger);
    }

    public RecordingKeyWrapProvider Provider { get; } = new();

    public CollectingLogger<EnvelopeEncryptor> Logger { get; } = new();

    public EnvelopeEncryptor Encryptor { get; }

    public Task<IDataKeySession> CreateSessionAsync() =>
        Encryptor.CreateDataKeyAsync(TestContext.Current.CancellationToken);

    public Task<IDataKeySession> OpenSessionAsync(WrappedDataKey wrappedKey) =>
        Encryptor.OpenDataKeyAsync(wrappedKey, TestContext.Current.CancellationToken);
}
