// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Encryption;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Application.Encryption;

[Trait("Category", "Security")]
public sealed class EncryptionModelsTests
{
    public static TheoryData<DecryptionFailureReason> AllReasons => new(Enum.GetValues<DecryptionFailureReason>());

    [Fact]
    public void WrappedDataKey_ToString_ShowsKeyIdAlgorithmAndLengthOnly()
    {
        var value = RandomNumberGenerator.GetBytes(384);
        var wrapped = new WrappedDataKey("https://vault.example/keys/k/v1", "RSA-OAEP-256", value);

        var text = wrapped.ToString();

        Assert.Equal("WrappedDataKey { KeyId = https://vault.example/keys/k/v1, Algorithm = RSA-OAEP-256, Length = 384 }", text);
        Assert.DoesNotContain(Convert.ToHexString(value), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(value), text, StringComparison.Ordinal);
    }

    [Fact]
    public void WrappedDataKey_ToStringWithNullValue_ShowsLengthZero()
    {
        var wrapped = new WrappedDataKey("id", "alg", null!);

        Assert.Equal("WrappedDataKey { KeyId = id, Algorithm = alg, Length = 0 }", wrapped.ToString());
    }

    [Theory]
    [MemberData(nameof(AllReasons))]
    public void SecretDecryptionException_AnyReason_UsesConstantMessage(DecryptionFailureReason reason)
    {
        var exception = new SecretDecryptionException(reason);

        Assert.Equal(EncryptionMessageText.SecretDecryption, exception.Message);
        Assert.Equal(SecretDecryptionException.DefaultMessage, exception.Message);
        Assert.Equal(reason, exception.Reason);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    [Fact]
    public void EncryptionContext_ToString_ContainsOnlyPurposeAndIds()
    {
        var context = EncryptionContexts.Random("secret.notes");

        var text = context.ToString();

        Assert.Equal($"EncryptionContext {{ Purpose = secret.notes, ItemId = {context.ItemId}, OwnerId = {context.OwnerId} }}", text);
    }
}
