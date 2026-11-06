// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text;
using NSubstitute;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Encryption;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

[Trait("Category", "Security")]
public sealed class DataKeySessionReEncryptToTests
{
    private const string Marker = "SV-TRANSFER-MARKER-77d2";
    private const string Purpose = "secret.name";
    private const int NonceIterations = 1000;

    [Fact]
    public async Task ReEncryptTo_RoundTrip_OpensUnderTheTargetSessionAndContext()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var itemId = Guid.NewGuid();
        var sourceContext = new EncryptionContext(Purpose, itemId, Guid.NewGuid());
        var targetContext = new EncryptionContext(Purpose, itemId, Guid.NewGuid());
        var envelope = source.Encrypt(sourceContext, Marker);

        var moved = source.ReEncryptTo(sourceContext, target, targetContext, envelope);

        Assert.Equal(Marker, target.Decrypt(targetContext, moved));
        Assert.Equal(Marker, source.Decrypt(sourceContext, envelope));
    }

    [Fact]
    public async Task ReEncryptTo_SameSession_Works()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var itemId = Guid.NewGuid();
        var sourceContext = new EncryptionContext(Purpose, itemId, Guid.NewGuid());
        var targetContext = new EncryptionContext(Purpose, itemId, Guid.NewGuid());
        var envelope = session.Encrypt(sourceContext, Marker);

        var moved = session.ReEncryptTo(sourceContext, session, targetContext, envelope);

        Assert.Equal(Marker, session.Decrypt(targetContext, moved));
    }

    [Fact]
    public async Task ReEncryptTo_EmptyPlaintext_Works()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var sourceContext = EncryptionContexts.Random();
        var targetContext = EncryptionContexts.Random();
        var envelope = source.Encrypt(sourceContext, string.Empty);

        var moved = source.ReEncryptTo(sourceContext, target, targetContext, envelope);

        Assert.Equal(string.Empty, target.Decrypt(targetContext, moved));
        Assert.Equal(EnvelopeFormat.Overhead, moved.Length);
    }

    [Fact]
    public async Task ReEncryptTo_MaximumEnvelope_Works()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var sourceContext = EncryptionContexts.Random();
        var targetContext = EncryptionContexts.Random();
        var text = new string('a', EnvelopeFormat.MaxPlaintextBytes);
        var envelope = source.Encrypt(sourceContext, text);

        var moved = source.ReEncryptTo(sourceContext, target, targetContext, envelope);

        Assert.Equal(EnvelopeFormat.MaxEnvelopeBytes, moved.Length);
        Assert.Equal(text, target.Decrypt(targetContext, moved));
    }

    [Fact]
    public async Task ReEncryptTo_NullArguments_Throw()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var context = EncryptionContexts.Random();
        var envelope = source.Encrypt(context, Marker);

        Assert.Throws<ArgumentNullException>(() => source.ReEncryptTo(null!, target, context, envelope));
        Assert.Throws<ArgumentNullException>(() => source.ReEncryptTo(context, null!, context, envelope));
        Assert.Throws<ArgumentNullException>(() => source.ReEncryptTo(context, target, null!, envelope));
        Assert.Throws<ArgumentNullException>(() => source.ReEncryptTo(context, target, context, null!));
    }

    [Fact]
    public async Task ReEncryptTo_WrongSourceContext_Throws()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var sourceContext = EncryptionContexts.Random();
        var envelope = source.Encrypt(sourceContext, Marker);
        var wrongContext = sourceContext with { OwnerId = Guid.NewGuid() };

        var exception = Assert.Throws<SecretDecryptionException>(() =>
            source.ReEncryptTo(wrongContext, target, EncryptionContexts.Random(), envelope));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ReEncryptTo_TamperedEnvelope_Throws(int part)
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var context = EncryptionContexts.Random();
        var envelope = source.Encrypt(context, Marker);
        var index = part switch
        {
            0 => EnvelopeFormat.VersionSize,
            1 => EnvelopeFormat.HeaderSize,
            _ => envelope.Length - 1,
        };
        envelope[index] ^= 0x01;

        Assert.Throws<SecretDecryptionException>(() =>
            source.ReEncryptTo(context, target, EncryptionContexts.Random(), envelope));
    }

    [Fact]
    public async Task ReEncryptTo_ThenOpenWithTheSourceKey_Fails()
    {
        var (harness, source, target) = await CreateTwoSessionsAsync();
        var sourceContext = EncryptionContexts.Random();
        var targetContext = EncryptionContexts.Random();
        var moved = source.ReEncryptTo(sourceContext, target, targetContext, source.Encrypt(sourceContext, Marker));
        var sourceWrapped = source.WrappedKey;
        source.Dispose();
        using var reopenedSource = await harness.OpenSessionAsync(sourceWrapped);

        Assert.Throws<SecretDecryptionException>(() => reopenedSource.Decrypt(targetContext, moved));
        Assert.Throws<SecretDecryptionException>(() => reopenedSource.Decrypt(sourceContext, moved));
        Assert.Equal(Marker, target.Decrypt(targetContext, moved));
    }

    [Fact]
    public async Task ReEncryptTo_RebindsAssociatedDataToTheNewOwner()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var itemId = Guid.NewGuid();
        var sourceContext = new EncryptionContext(Purpose, itemId, Guid.NewGuid());
        var targetContext = new EncryptionContext(Purpose, itemId, Guid.NewGuid());
        var envelope = source.Encrypt(sourceContext, Marker);

        var moved = source.ReEncryptTo(sourceContext, target, targetContext, envelope);

        Assert.Equal(Marker, target.Decrypt(targetContext, moved));
        Assert.Throws<SecretDecryptionException>(() => target.Decrypt(sourceContext, moved));
        Assert.Throws<SecretDecryptionException>(() => target.Decrypt(targetContext with { OwnerId = Guid.NewGuid() }, moved));
        Assert.Throws<SecretDecryptionException>(() => target.Decrypt(targetContext with { ItemId = Guid.NewGuid() }, moved));
        Assert.Throws<SecretDecryptionException>(() => target.Decrypt(targetContext with { Purpose = "secret.url" }, moved));
    }

    [Fact]
    public async Task ReEncryptTo_SameSession_SourceEnvelopeIsUnreadableWithTheNewContext()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var itemId = Guid.NewGuid();
        var sourceContext = new EncryptionContext(Purpose, itemId, Guid.NewGuid());
        var targetContext = new EncryptionContext(Purpose, itemId, Guid.NewGuid());
        var envelope = session.Encrypt(sourceContext, Marker);

        var moved = session.ReEncryptTo(sourceContext, session, targetContext, envelope);

        Assert.Throws<SecretDecryptionException>(() => session.Decrypt(targetContext, envelope));
        Assert.Throws<SecretDecryptionException>(() => session.Decrypt(sourceContext, moved));
        Assert.Equal(Marker, session.Decrypt(sourceContext, envelope));
    }

    [Fact]
    public async Task ReEncryptTo_Output_ContainsNoPlaintext()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var sourceContext = EncryptionContexts.Random();
        var targetContext = EncryptionContexts.Random();
        var envelope = source.Encrypt(sourceContext, Marker);
        var markerBytes = Encoding.UTF8.GetBytes(Marker);

        var moved = source.ReEncryptTo(sourceContext, target, targetContext, envelope);

        Assert.True(moved.AsSpan().IndexOf(markerBytes) < 0);
        Assert.True(envelope.AsSpan().IndexOf(markerBytes) < 0);
        Assert.Equal(envelope.Length, moved.Length);
    }

    [Fact]
    public async Task ReEncryptTo_NewNonceEveryCall()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var sourceContext = EncryptionContexts.Random();
        var targetContext = EncryptionContexts.Random();
        var envelope = source.Encrypt(sourceContext, Marker);
        var nonces = new HashSet<string> { Convert.ToHexString(Nonce(envelope)) };

        for (var i = 0; i < NonceIterations; i++)
        {
            var moved = source.ReEncryptTo(sourceContext, target, targetContext, envelope);
            Assert.True(nonces.Add(Convert.ToHexString(Nonce(moved))));
        }
    }

    [Fact]
    public async Task ReEncryptTo_DisposedSourceOrTarget_Throws()
    {
        var (_, source, target) = await CreateTwoSessionsAsync();
        var context = EncryptionContexts.Random();
        var envelope = source.Encrypt(context, Marker);

        target.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.ReEncryptTo(context, target, context, envelope));

        source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.ReEncryptTo(context, target, context, envelope));
    }

    [Fact]
    public async Task ReEncryptTo_ForeignSessionType_Throws()
    {
        var harness = new EncryptionHarness();
        using var source = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = source.Encrypt(context, Marker);
        var foreign = Substitute.For<IDataKeySession>();

        Assert.Throws<ArgumentException>(() => source.ReEncryptTo(context, foreign, context, envelope));
    }

    [Fact]
    public async Task ReEncryptTo_Dispose_ZeroesBothDataKeys()
    {
        var (harness, source, target) = await CreateTwoSessionsAsync();
        var sourceContext = EncryptionContexts.Random();
        var targetContext = EncryptionContexts.Random();
        var keys = harness.Provider.WrapInputs;
        ByteAssert.NotAllZero(keys[0]);
        ByteAssert.NotAllZero(keys[1]);

        source.ReEncryptTo(sourceContext, target, targetContext, source.Encrypt(sourceContext, Marker));
        source.Dispose();
        target.Dispose();

        ByteAssert.AllZero(keys[0]);
        ByteAssert.AllZero(keys[1]);
    }

    private static byte[] Nonce(byte[] envelope) =>
        envelope.AsSpan(EnvelopeFormat.VersionSize, EnvelopeFormat.NonceSize).ToArray();

    private static async Task<(EncryptionHarness Harness, IDataKeySession Source, IDataKeySession Target)> CreateTwoSessionsAsync()
    {
        var harness = new EncryptionHarness();
        var source = await harness.CreateSessionAsync();
        var target = await harness.CreateSessionAsync();
        return (harness, source, target);
    }
}
