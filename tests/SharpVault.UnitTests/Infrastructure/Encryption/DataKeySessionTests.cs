// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Infrastructure.Encryption;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

public sealed class DataKeySessionTests
{
    public static TheoryData<string> RoundTripTexts => new()
    {
        string.Empty,
        "pässwörd 密码 😀",
        new string('a', EnvelopeFormat.MaxPlaintextBytes),
        string.Concat(Enumerable.Repeat("😀", EnvelopeFormat.MaxPlaintextBytes / 4)),
    };

    public static TheoryData<string> InvalidContextKinds => new()
    {
        "purpose",
        "item",
        "owner",
    };

    [Theory]
    [MemberData(nameof(RoundTripTexts))]
    public async Task EncryptThenDecrypt_SameContext_ReturnsOriginalText(string text)
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();

        var envelope = session.Encrypt(context, text);

        Assert.Equal(text, session.Decrypt(context, envelope));
    }

    [Fact]
    public async Task Encrypt_Envelope_HasVersionOneAndLengthOfPlaintextBytesPlusOverhead()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        const string text = "pässwörd 密码";

        var envelope = session.Encrypt(EncryptionContexts.Random(), text);

        Assert.Equal(EnvelopeFormat.Version1, envelope[0]);
        Assert.Equal(Encoding.UTF8.GetByteCount(text) + EnvelopeFormat.Overhead, envelope.Length);
    }

    [Fact]
    public async Task Decrypt_EnvelopeBuiltIndependentlyWithAesGcm_ReturnsPlaintext()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var key = harness.Provider.WrapInputs[0].ToArray();
        var context = EncryptionContexts.Random("folder.name");
        const string text = "Independent ünïcode 😀";
        var plaintext = Encoding.UTF8.GetBytes(text);
        var associatedData = BuildAssociatedData(context);
        var envelope = new byte[1 + 12 + plaintext.Length + 16];
        envelope[0] = 0x01;
        RandomNumberGenerator.Fill(envelope.AsSpan(1, 12));

        using (var aes = new AesGcm(key, 16))
        {
            aes.Encrypt(
                envelope.AsSpan(1, 12),
                plaintext,
                envelope.AsSpan(13, plaintext.Length),
                envelope.AsSpan(13 + plaintext.Length, 16),
                associatedData);
        }

        Assert.Equal(text, session.Decrypt(context, envelope));
    }

    [Fact]
    public async Task Encrypt_PrecomposedAndDecomposedText_RoundTripsByteExact()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        const string precomposed = "é";
        const string decomposed = "é";

        var first = session.Encrypt(context, precomposed);
        var second = session.Encrypt(context, decomposed);

        Assert.Equal(precomposed, session.Decrypt(context, first));
        Assert.Equal(decomposed, session.Decrypt(context, second));
        Assert.NotEqual(first.Length, second.Length);
    }

    [Fact]
    public async Task Encrypt_PlaintextExactlyMaximumBytes_ProducesMaximumEnvelope()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();

        var envelope = session.Encrypt(EncryptionContexts.Random(), new string('x', EnvelopeFormat.MaxPlaintextBytes));

        Assert.Equal(EnvelopeFormat.MaxEnvelopeBytes, envelope.Length);
    }

    [Fact]
    public async Task ReEncrypt_ToNewOwner_DecryptsOnlyWithNewOwnerContext()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var itemId = Guid.NewGuid();
        var source = new EncryptionContext("secret.value", itemId, Guid.NewGuid());
        var target = source with { OwnerId = Guid.NewGuid() };
        var envelope = session.Encrypt(source, "to-be-transferred");

        var reEncrypted = session.ReEncrypt(source, target, envelope);

        Assert.Equal("to-be-transferred", session.Decrypt(target, reEncrypted));
        var failure = Assert.Throws<SecretDecryptionException>(() => session.Decrypt(source, reEncrypted));
        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, failure.Reason);
    }

    [Fact]
    public async Task ReEncrypt_ToNewOwner_LeavesSourceEnvelopeUnchangedAndStillDecryptable()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var source = EncryptionContexts.Random();
        var target = source with { OwnerId = Guid.NewGuid() };
        var envelope = session.Encrypt(source, "unchanged");
        var copy = envelope.ToArray();

        session.ReEncrypt(source, target, envelope);

        Assert.Equal(copy, envelope);
        Assert.Equal("unchanged", session.Decrypt(source, envelope));
    }

    [Fact]
    public async Task ReEncrypt_Always_MakesNoKeyWrapCalls()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var wrapCount = harness.Provider.WrapCount;
        var unwrapCount = harness.Provider.UnwrapCount;
        var source = EncryptionContexts.Random();
        var envelope = session.Encrypt(source, "x");

        session.ReEncrypt(source, source with { OwnerId = Guid.NewGuid() }, envelope);

        Assert.Equal(wrapCount, harness.Provider.WrapCount);
        Assert.Equal(unwrapCount, harness.Provider.UnwrapCount);
    }

    [Fact]
    public async Task Operations_AfterDispose_ThrowObjectDisposedException()
    {
        var harness = new EncryptionHarness();
        var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "x");
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.Encrypt(context, "x"));
        Assert.Throws<ObjectDisposedException>(() => session.Decrypt(context, envelope));
        Assert.Throws<ObjectDisposedException>(() => session.ReEncrypt(context, context, envelope));
    }

    [Theory]
    [MemberData(nameof(InvalidContextKinds))]
    public async Task Encrypt_InvalidContext_ThrowsBeforeEncrypting(string kind)
    {
        var context = InvalidContext(kind);
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();

        Assert.Throws<ArgumentException>(() => session.Encrypt(context, "x"));
    }

    [Theory]
    [MemberData(nameof(InvalidContextKinds))]
    public async Task ReEncrypt_InvalidTargetContext_ThrowsArgumentExceptionBeforeDecrypting(string kind)
    {
        var target = InvalidContext(kind);
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var source = EncryptionContexts.Random();
        var envelope = session.Encrypt(source, "x");
        envelope[^1] ^= 0xFF;

        Assert.Throws<ArgumentException>(() => session.ReEncrypt(source, target, envelope));
        Assert.Empty(harness.Logger.Entries);
    }

    private static EncryptionContext InvalidContext(string kind) => kind switch
    {
        "purpose" => new EncryptionContext("Bad Purpose", Guid.NewGuid(), Guid.NewGuid()),
        "item" => new EncryptionContext("secret.value", Guid.Empty, Guid.NewGuid()),
        _ => new EncryptionContext("secret.value", Guid.NewGuid(), Guid.Empty),
    };

    private static byte[] BuildAssociatedData(EncryptionContext context)
    {
        var purpose = Encoding.ASCII.GetBytes(context.Purpose);
        var data = new List<byte> { 0x01 };
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)purpose.Length);
        data.AddRange(length);
        data.AddRange(purpose);
        BinaryPrimitives.WriteUInt16BigEndian(length, 16);
        data.AddRange(length);
        data.AddRange(context.ItemId.ToByteArray(bigEndian: true));
        data.AddRange(length);
        data.AddRange(context.OwnerId.ToByteArray(bigEndian: true));
        return [.. data];
    }
}
