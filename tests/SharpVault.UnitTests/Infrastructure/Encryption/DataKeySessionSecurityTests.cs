// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Infrastructure.Encryption;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

[Trait("Category", "Security")]
public sealed class DataKeySessionSecurityTests
{
    private const string PlaintextMarker = "SV-PLAINTEXT-5c1e";
    private const int NonceUniquenessIterations = 100_000;
    private const int SampleEnvelopePlaintextBytes = 64;

    public static TheoryData<byte> UnknownVersionBytes
    {
        get
        {
            var data = new TheoryData<byte>();
            for (var value = 0; value <= byte.MaxValue; value++)
            {
                if (value != EnvelopeFormat.Version1)
                {
                    data.Add((byte)value);
                }
            }

            return data;
        }
    }

    public static TheoryData<int> ShortEnvelopeLengths
    {
        get
        {
            var data = new TheoryData<int>();
            for (var length = 0; length < EnvelopeFormat.Overhead; length++)
            {
                data.Add(length);
            }

            return data;
        }
    }

    public static TheoryData<int> SurrogateCases => new() { 0, 1, 2, 3 };

    [Fact]
    public async Task Encrypt_HundredThousandTimes_NeverRepeatsNonce()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var nonces = new HashSet<string>(NonceUniquenessIterations, StringComparer.Ordinal);

        for (var i = 0; i < NonceUniquenessIterations; i++)
        {
            var envelope = session.Encrypt(context, "x");
            Assert.True(nonces.Add(Convert.ToHexString(envelope.AsSpan(1, EnvelopeFormat.NonceSize))));
        }

        Assert.Equal(NonceUniquenessIterations, nonces.Count);
    }

    [Fact]
    public async Task Encrypt_NonceAcrossManySessions_NeverRepeats()
    {
        var harness = new EncryptionHarness();
        var context = EncryptionContexts.Random();
        var nonces = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 500; i++)
        {
            using var session = await harness.CreateSessionAsync();
            for (var j = 0; j < 20; j++)
            {
                Assert.True(nonces.Add(Convert.ToHexString(session.Encrypt(context, "x").AsSpan(1, EnvelopeFormat.NonceSize))));
            }
        }
    }

    [Fact]
    public async Task Encrypt_SameTextTwice_ProducesDifferentEnvelopes()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();

        var first = session.Encrypt(context, "same");
        var second = session.Encrypt(context, "same");

        Assert.NotEqual(first, second);
        Assert.NotEqual(first[1..13], second[1..13]);
    }

    [Fact]
    public async Task ReEncrypt_ToNewOwner_ProducesFreshNonce()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var source = EncryptionContexts.Random();
        var target = source with { OwnerId = Guid.NewGuid() };
        var envelope = session.Encrypt(source, "x");

        var reEncrypted = session.ReEncrypt(source, target, envelope);
        var again = session.ReEncrypt(source, target, envelope);

        Assert.NotEqual(envelope[1..13], reEncrypted[1..13]);
        Assert.NotEqual(reEncrypted[1..13], again[1..13]);
    }

    [Fact]
    public async Task Decrypt_DifferentOwner_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context with { OwnerId = Guid.NewGuid() }, envelope));
    }

    [Fact]
    public async Task Decrypt_DifferentItem_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context with { ItemId = Guid.NewGuid() }, envelope));
    }

    [Fact]
    public async Task Decrypt_DifferentPurpose_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random("secret.notes");
        var envelope = session.Encrypt(context, "secret");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context with { Purpose = "secret.value" }, envelope));
    }

    [Fact]
    public async Task Decrypt_ItemAndOwnerSwapped_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");
        var swapped = context with { ItemId = context.OwnerId, OwnerId = context.ItemId };

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(swapped, envelope));
    }

    [Fact]
    public async Task Decrypt_EveryContextComponentChangedAtOnce_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var envelope = session.Encrypt(EncryptionContexts.Random("secret.notes"), "secret");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(EncryptionContexts.Random("secret.value"), envelope));
    }

    [Fact]
    public async Task Decrypt_EnvelopeWithDifferentVersionInAssociatedData_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var key = harness.Provider.WrapInputs[0].ToArray();
        var context = EncryptionContexts.Random();
        var plaintext = Encoding.UTF8.GetBytes("secret");
        var associatedData = new byte[EnvelopeAssociatedData.MaxLength];
        var written = EnvelopeAssociatedData.Write(0x02, context, associatedData);
        var envelope = new byte[EnvelopeFormat.Overhead + plaintext.Length];
        envelope[0] = EnvelopeFormat.Version1;
        RandomNumberGenerator.Fill(envelope.AsSpan(1, EnvelopeFormat.NonceSize));
        using (var aes = new AesGcm(key, EnvelopeFormat.TagSize))
        {
            aes.Encrypt(
                envelope.AsSpan(1, EnvelopeFormat.NonceSize),
                plaintext,
                envelope.AsSpan(EnvelopeFormat.HeaderSize, plaintext.Length),
                envelope.AsSpan(envelope.Length - EnvelopeFormat.TagSize),
                associatedData.AsSpan(0, written));
        }

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context, envelope));
    }

    [Fact]
    public async Task Decrypt_AuthenticatedInvalidUtf8_ThrowsMalformedEnvelopeWithoutBytes()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var key = harness.Provider.WrapInputs[0].ToArray();
        var context = EncryptionContexts.Random();
        byte[] plaintext = [0x68, 0xFF, 0xFE];
        var associatedData = new byte[EnvelopeAssociatedData.MaxLength];
        var written = EnvelopeAssociatedData.Write(EnvelopeFormat.Version1, context, associatedData);
        var envelope = new byte[EnvelopeFormat.Overhead + plaintext.Length];
        envelope[0] = EnvelopeFormat.Version1;
        RandomNumberGenerator.Fill(envelope.AsSpan(1, EnvelopeFormat.NonceSize));
        using (var aes = new AesGcm(key, EnvelopeFormat.TagSize))
        {
            aes.Encrypt(
                envelope.AsSpan(1, EnvelopeFormat.NonceSize),
                plaintext,
                envelope.AsSpan(EnvelopeFormat.HeaderSize, plaintext.Length),
                envelope.AsSpan(envelope.Length - EnvelopeFormat.TagSize),
                associatedData.AsSpan(0, written));
        }

        var exception = Assert.Throws<SecretDecryptionException>(() => session.Decrypt(context, envelope));

        Assert.Equal(DecryptionFailureReason.MalformedEnvelope, exception.Reason);
        Assert.Null(exception.InnerException);
        var text = exception.ToString();
        Assert.DoesNotMatch(@"(?i)\b(0x)?(FF|FE|255|254)\b", text);
        Assert.DoesNotMatch(@"\[\s*(0x)?[0-9A-Fa-f]{1,3}\s*(,\s*(0x)?[0-9A-Fa-f]{1,3}\s*)*\]", text);
    }

    [Fact]
    public async Task Decrypt_EnvelopeSwappedBetweenItemsWithSameDataKey_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var owner = Guid.NewGuid();
        var itemA = new EncryptionContext("secret.value", Guid.NewGuid(), owner);
        var itemB = new EncryptionContext("secret.value", Guid.NewGuid(), owner);
        var envelopeA = session.Encrypt(itemA, "a-value");
        var envelopeB = session.Encrypt(itemB, "b-value");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(itemB, envelopeA));
        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(itemA, envelopeB));
    }

    [Fact]
    public async Task Decrypt_EnvelopeFromAnotherDataKey_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var first = await harness.CreateSessionAsync();
        using var second = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = first.Encrypt(context, "secret");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => second.Decrypt(context, envelope));
    }

    [Fact]
    public async Task Decrypt_AnySingleBitFlipped_ThrowsExpectedReason()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, new string('p', SampleEnvelopePlaintextBytes));

        for (var index = 0; index < envelope.Length; index++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                var tampered = envelope.ToArray();
                tampered[index] ^= (byte)(1 << bit);
                var expected = index == 0 ? DecryptionFailureReason.UnsupportedVersion : DecryptionFailureReason.AuthenticationFailed;

                var failure = Assert.Throws<SecretDecryptionException>(() => session.Decrypt(context, tampered));

                Assert.True(expected == failure.Reason, $"byte {index} bit {bit}: expected {expected}, got {failure.Reason}");
            }
        }
    }

    [Fact]
    public async Task Decrypt_EveryByteReplaced_ThrowsExpectedReason()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, new string('p', 24));

        for (var index = 1; index < envelope.Length; index++)
        {
            var tampered = envelope.ToArray();
            tampered[index] = (byte)(tampered[index] + 1);

            AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context, tampered));
        }
    }

    [Fact]
    public async Task Decrypt_TamperedEnvelope_DoesNotReturnPartialPlaintext()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, PlaintextMarker);
        envelope[EnvelopeFormat.HeaderSize] ^= 0x01;

        string? result = null;
        var failure = Record.Exception(() => result = session.Decrypt(context, envelope));

        Assert.IsType<SecretDecryptionException>(failure);
        Assert.Null(result);
    }

    [Fact]
    public async Task Decrypt_TagReplacedWithZeros_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");
        Array.Clear(envelope, envelope.Length - EnvelopeFormat.TagSize, EnvelopeFormat.TagSize);

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context, envelope));
    }

    [Fact]
    public async Task Decrypt_NonceReplacedWithZeros_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");
        Array.Clear(envelope, EnvelopeFormat.VersionSize, EnvelopeFormat.NonceSize);

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context, envelope));
    }

    [Fact]
    public async Task Decrypt_ExtraByteAppended_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context, [.. envelope, 0x00]));
    }

    [Fact]
    public async Task Decrypt_ExtraByteInsertedAfterVersion_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");
        byte[] inserted = [envelope[0], 0x00, .. envelope[1..]];

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context, inserted));
    }

    [Theory]
    [MemberData(nameof(UnknownVersionBytes))]
    public async Task Decrypt_UnknownVersionByte_ThrowsUnsupportedVersion(byte version)
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");
        envelope[0] = version;

        AssertReason(DecryptionFailureReason.UnsupportedVersion, () => session.Decrypt(context, envelope));
    }

    [Fact]
    public async Task ReEncrypt_UnknownVersionByte_ThrowsUnsupportedVersionAndLogsOnce()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var source = EncryptionContexts.Random();
        var envelope = session.Encrypt(source, "secret");
        envelope[0] = 0x80;

        AssertReason(DecryptionFailureReason.UnsupportedVersion, () => session.ReEncrypt(source, source with { OwnerId = Guid.NewGuid() }, envelope));
        Assert.Single(harness.Logger.Entries);
    }

    [Theory]
    [MemberData(nameof(ShortEnvelopeLengths))]
    public async Task Decrypt_EnvelopeShorterThanOverhead_ThrowsMalformedEnvelope(int length)
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var envelope = new byte[length];
        if (length > 0)
        {
            envelope[0] = EnvelopeFormat.Version1;
        }

        AssertReason(DecryptionFailureReason.MalformedEnvelope, () => session.Decrypt(EncryptionContexts.Random(), envelope));
    }

    [Fact]
    public async Task Decrypt_EnvelopeTruncatedByOneByte_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(context, envelope[..^1]));
    }

    [Fact]
    public async Task Decrypt_TruncatedAtEveryLength_ThrowsMalformedOrAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, new string('t', 40));

        for (var length = 0; length < envelope.Length; length++)
        {
            var truncated = envelope[..length];
            var expected = length < EnvelopeFormat.Overhead ? DecryptionFailureReason.MalformedEnvelope : DecryptionFailureReason.AuthenticationFailed;

            AssertReason(expected, () => session.Decrypt(context, truncated));
        }
    }

    [Fact]
    public async Task Decrypt_HeaderOnlyBoundaries_ThrowExpectedReason()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, "secret");

        AssertReason(DecryptionFailureReason.MalformedEnvelope, () => session.Decrypt(context, envelope[..EnvelopeFormat.VersionSize]));
        AssertReason(DecryptionFailureReason.MalformedEnvelope, () => session.Decrypt(context, envelope[..EnvelopeFormat.HeaderSize]));
        AssertReason(DecryptionFailureReason.MalformedEnvelope, () => session.Decrypt(context, envelope[..(EnvelopeFormat.HeaderSize + EnvelopeFormat.TagSize - 1)]));
    }

    [Fact]
    public async Task Decrypt_EnvelopeOverMaximumLength_ThrowsMalformedEnvelope()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var envelope = new byte[EnvelopeFormat.MaxEnvelopeBytes + 1];
        envelope[0] = EnvelopeFormat.Version1;

        AssertReason(DecryptionFailureReason.MalformedEnvelope, () => session.Decrypt(EncryptionContexts.Random(), envelope));
    }

    [Fact]
    public async Task Decrypt_EnvelopeExactlyMaximumLengthWithBadTag_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var envelope = new byte[EnvelopeFormat.MaxEnvelopeBytes];
        envelope[0] = EnvelopeFormat.Version1;

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.Decrypt(EncryptionContexts.Random(), envelope));
    }

    [Fact]
    public async Task Decrypt_EmptyEnvelope_ThrowsMalformedEnvelope()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();

        AssertReason(DecryptionFailureReason.MalformedEnvelope, () => session.Decrypt(EncryptionContexts.Random(), []));
    }

    [Fact]
    public async Task Encrypt_PlaintextOverMaximumBytes_ThrowsWithoutPlaintextInMessage()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var plaintext = PlaintextMarker + new string('a', EnvelopeFormat.MaxPlaintextBytes + 1 - PlaintextMarker.Length);
        Assert.Equal(EnvelopeFormat.MaxPlaintextBytes + 1, plaintext.Length);

        var exception = Assert.Throws<ArgumentException>(() => session.Encrypt(EncryptionContexts.Random(), plaintext));

        Assert.Equal(EncryptionMessageText.ForParameter(EncryptionMessageText.PlaintextTooLarge, "plaintext"), exception.Message);
        Assert.DoesNotContain(PlaintextMarker, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Encrypt_MultiByteTextOverMaximumBytes_Throws()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var text = new string('€', 10_923);

        var exception = Assert.Throws<ArgumentException>(() => session.Encrypt(EncryptionContexts.Random(), text));

        Assert.Equal(EncryptionMessageText.ForParameter(EncryptionMessageText.PlaintextTooLarge, "plaintext"), exception.Message);
    }

    [Fact]
    public async Task Encrypt_MultiByteTextExactlyMaximumBytes_Succeeds()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var text = new string('€', 10_922) + "ab";
        Assert.Equal(EnvelopeFormat.MaxPlaintextBytes, Encoding.UTF8.GetByteCount(text));

        var envelope = session.Encrypt(context, text);

        Assert.Equal(text, session.Decrypt(context, envelope));
    }

    [Theory]
    [MemberData(nameof(SurrogateCases))]
    public async Task Encrypt_LoneSurrogate_ThrowsArgumentException(int surrogateCase)
    {
        string[] texts = ["\ud800", "a\udc00b", "abc\ud83d", "\udc00\ud800"];
        var text = texts[surrogateCase];
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();

        var exception = Assert.Throws<ArgumentException>(() => session.Encrypt(EncryptionContexts.Random(), text));

        Assert.Equal(EncryptionMessageText.ForParameter(EncryptionMessageText.PlaintextNotValidUnicode, "plaintext"), exception.Message);
    }

    [Fact]
    public async Task ReEncrypt_TamperedSource_ThrowsAndReturnsNothing()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var source = EncryptionContexts.Random();
        var envelope = session.Encrypt(source, "secret");
        envelope[EnvelopeFormat.HeaderSize] ^= 0x01;
        byte[]? result = null;

        var failure = Record.Exception(() => result = session.ReEncrypt(source, source with { OwnerId = Guid.NewGuid() }, envelope));

        var typed = Assert.IsType<SecretDecryptionException>(failure);
        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, typed.Reason);
        Assert.Null(result);
    }

    [Fact]
    public async Task ReEncrypt_WrongSourceContext_ThrowsAuthenticationFailed()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var source = EncryptionContexts.Random();
        var envelope = session.Encrypt(source, "secret");

        AssertReason(DecryptionFailureReason.AuthenticationFailed, () => session.ReEncrypt(source with { OwnerId = Guid.NewGuid() }, source, envelope));
    }

    [Fact]
    public async Task ReEncrypt_EnvelopeOverMaximumLength_ThrowsMalformedEnvelope()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var source = EncryptionContexts.Random();
        var envelope = new byte[EnvelopeFormat.MaxEnvelopeBytes + 1];
        envelope[0] = EnvelopeFormat.Version1;

        AssertReason(DecryptionFailureReason.MalformedEnvelope, () => session.ReEncrypt(source, source, envelope));
    }

    [Fact]
    public async Task Decrypt_FailedAuthentication_LogsIdsAndReasonWithoutData()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random("secret.notes");
        var envelope = session.Encrypt(context, PlaintextMarker);
        envelope[^1] ^= 0x01;

        Assert.Throws<SecretDecryptionException>(() => session.Decrypt(context, envelope));

        var entry = Assert.Single(harness.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(context.ItemId.ToString(), entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secret.notes", entry.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DecryptionFailureReason.AuthenticationFailed), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PlaintextMarker, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(envelope), entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(envelope), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(context.OwnerId.ToString(), entry.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_SuccessfulOperations_LogNothing()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var source = EncryptionContexts.Random();
        var target = source with { OwnerId = Guid.NewGuid() };

        var envelope = session.Encrypt(source, PlaintextMarker);
        session.Decrypt(source, envelope);
        session.ReEncrypt(source, target, envelope);

        Assert.Empty(harness.Logger.Entries);
    }

    [Fact]
    public async Task Dispose_AfterCreate_ZeroesTheDataKeyPassedToWrap()
    {
        var harness = new EncryptionHarness();
        var session = await harness.CreateSessionAsync();
        var key = harness.Provider.WrapInputs[0];
        ByteAssert.NotAllZero(key);

        session.Dispose();

        ByteAssert.AllZero(key);
    }

    [Fact]
    public async Task Dispose_AfterOpen_ZeroesTheDataKeyReturnedByUnwrap()
    {
        var harness = new EncryptionHarness();
        var created = await harness.CreateSessionAsync();
        var wrapped = created.WrappedKey;
        created.Dispose();
        var session = await harness.OpenSessionAsync(wrapped);
        var key = harness.Provider.UnwrapOutputs[0];
        ByteAssert.NotAllZero(key);

        session.Dispose();

        ByteAssert.AllZero(key);
    }

    [Fact]
    public async Task DataKey_BeforeDispose_IsNotAllZeros()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();

        ByteAssert.NotAllZero(harness.Provider.WrapInputs[0]);
    }

    [Fact]
    public async Task Dispose_Twice_KeepsKeyZeroed()
    {
        var harness = new EncryptionHarness();
        var session = await harness.CreateSessionAsync();
        var key = harness.Provider.WrapInputs[0];

        session.Dispose();
        session.Dispose();

        ByteAssert.AllZero(key);
    }

    [Fact]
    public async Task Decrypt_AfterSuccess_ZeroesPooledPlaintextBuffer()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, PlaintextMarker);
        var markerBytes = Encoding.UTF8.GetBytes(PlaintextMarker);

        session.Decrypt(context, envelope);

        var probe = System.Buffers.ArrayPool<byte>.Shared.Rent(envelope.Length - EnvelopeFormat.Overhead);
        try
        {
            Assert.False(probe.AsSpan().IndexOf(markerBytes) >= 0);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(probe);
        }
    }

    [Fact]
    public async Task DataKeySession_ToString_ContainsNoKeyOrPlaintext()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var key = harness.Provider.WrapInputs[0];
        var context = EncryptionContexts.Random();
        session.Encrypt(context, PlaintextMarker);

        var text = session.ToString() ?? string.Empty;

        Assert.DoesNotContain(PlaintextMarker, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(key), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(key), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exceptions_FromEveryFailurePath_ContainNoPlaintextKeyOrEnvelope()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var key = harness.Provider.WrapInputs[0].ToArray();
        var context = EncryptionContexts.Random();
        var envelope = session.Encrypt(context, PlaintextMarker);
        var exceptions = new List<Exception>();

        void Collect(Action action) => exceptions.Add(Assert.ThrowsAny<Exception>(action));

        var flipped = envelope.ToArray();
        flipped[^1] ^= 0x01;
        var wrongVersion = envelope.ToArray();
        wrongVersion[0] = 0x80;
        Collect(() => session.Decrypt(context, flipped));
        Collect(() => session.Decrypt(context with { OwnerId = Guid.NewGuid() }, envelope));
        Collect(() => session.Decrypt(context, wrongVersion));
        Collect(() => session.Decrypt(context, envelope[..10]));
        Collect(() => session.Decrypt(context, envelope[..^1]));
        Collect(() => session.ReEncrypt(context, context, flipped));
        Collect(() => session.Encrypt(context, PlaintextMarker + new string('a', EnvelopeFormat.MaxPlaintextBytes)));
        Collect(() => session.Encrypt(context, PlaintextMarker + "\ud800"));
        Collect(() => session.Encrypt(context with { Purpose = PlaintextMarker }, PlaintextMarker));
        var disposed = await harness.CreateSessionAsync();
        disposed.Dispose();
        Collect(() => disposed.Encrypt(context, PlaintextMarker));

        var forbidden = new[]
        {
            PlaintextMarker,
            Convert.ToHexString(key),
            Convert.ToBase64String(key),
            Convert.ToHexString(envelope),
            Convert.ToBase64String(envelope),
        };

        foreach (var exception in exceptions)
        {
            var text = exception.ToString();
            foreach (var fragment in forbidden)
            {
                Assert.DoesNotContain(fragment, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static void AssertReason(DecryptionFailureReason expected, Action action)
    {
        var failure = Assert.Throws<SecretDecryptionException>(action);
        Assert.Equal(expected, failure.Reason);
    }
}
