// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text;
using SharpVault.Application.Common.Encryption;
using SharpVault.Infrastructure.Encryption;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

public sealed class EnvelopeAssociatedDataTests
{
    private const string GoldenHex = "01000c7365637265742e76616c7565" + "0010" + "00112233445566778899aabbccddeeff" + "0010" + "ffeeddccbbaa99887766554433221100";
    private static readonly Guid GoldenItem = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid GoldenOwner = Guid.Parse("ffeeddcc-bbaa-9988-7766-554433221100");

    public static TheoryData<string> InvalidPurposes => new()
    {
        "",
        " ",
        "Secret.value",
        "1secret",
        ".secret",
        "-secret",
        "secret value",
        "secret/value",
        "secret_value",
        "sécret",
        new string('a', 65),
        "secret.value\n",
        "' or PartitionKey ne '",
        "secret\0value",
        "secret.value​",
    };

    public static TheoryData<string> ValidPurposes => new()
    {
        "a",
        "secret.value",
        "folder.name",
        "secret.history-value",
        new string('a', 64),
    };

    [Fact]
    public void Write_KnownContext_ProducesDocumentedByteLayout()
    {
        var context = new EncryptionContext("secret.value", GoldenItem, GoldenOwner);
        var buffer = new byte[EnvelopeAssociatedData.MaxLength];

        var written = EnvelopeAssociatedData.Write(EnvelopeFormat.Version1, context, buffer);

        Assert.Equal(GoldenHex, Convert.ToHexStringLower(buffer.AsSpan(0, written)));
    }

    [Fact]
    public void Write_MaximumLengthPurpose_ReturnsMaxLength()
    {
        var context = new EncryptionContext(new string('a', EnvelopeAssociatedData.MaxPurposeLength), GoldenItem, GoldenOwner);
        var buffer = new byte[EnvelopeAssociatedData.MaxLength];

        var written = EnvelopeAssociatedData.Write(EnvelopeFormat.Version1, context, buffer);

        Assert.Equal(EnvelopeAssociatedData.MaxLength, written);
        Assert.Equal(103, written);
    }

    [Fact]
    public void Write_DifferentVersion_ProducesDifferentBytes()
    {
        var context = EncryptionContexts.Random();
        var first = new byte[EnvelopeAssociatedData.MaxLength];
        var second = new byte[EnvelopeAssociatedData.MaxLength];

        var firstLength = EnvelopeAssociatedData.Write(0x01, context, first);
        var secondLength = EnvelopeAssociatedData.Write(0x02, context, second);

        Assert.Equal(firstLength, secondLength);
        Assert.NotEqual(first.AsSpan(0, firstLength).ToArray(), second.AsSpan(0, secondLength).ToArray());
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("item")]
    [InlineData("owner")]
    [InlineData("swappedIds")]
    [Trait("Category", "Security")]
    public void Write_DifferentPurposeItemOrOwner_ProducesDifferentBytes(string changed)
    {
        var baseline = new EncryptionContext("secret.value", GoldenItem, GoldenOwner);
        var other = changed switch
        {
            "purpose" => baseline with { Purpose = "secret.notes" },
            "item" => baseline with { ItemId = Guid.NewGuid() },
            "owner" => baseline with { OwnerId = Guid.NewGuid() },
            _ => baseline with { ItemId = GoldenOwner, OwnerId = GoldenItem },
        };

        Assert.NotEqual(Encode(baseline), Encode(other));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Write_PurposeLengthsThatShiftBoundaries_DoNotCollide()
    {
        var item = GoldenItem;
        var owner = GoldenOwner;
        var first = Encode(new EncryptionContext("ab", item, owner));
        var second = Encode(new EncryptionContext("a", item, owner));

        Assert.NotEqual(first, second);
        Assert.NotEqual(first.Length, second.Length);
    }

    [Theory]
    [MemberData(nameof(ValidPurposes))]
    public void EnsureValid_ValidPurpose_DoesNotThrow(string purpose)
    {
        var context = new EncryptionContext(purpose, Guid.NewGuid(), Guid.NewGuid());

        var exception = Record.Exception(() => EnvelopeAssociatedData.EnsureValid(context, nameof(context)));

        Assert.Null(exception);
    }

    [Theory]
    [MemberData(nameof(InvalidPurposes))]
    [Trait("Category", "Security")]
    public void EnsureValid_InvalidPurpose_ThrowsArgumentException(string purpose)
    {
        var context = new EncryptionContext(purpose, Guid.NewGuid(), Guid.NewGuid());

        var exception = Assert.Throws<ArgumentException>(() => EnvelopeAssociatedData.EnsureValid(context, nameof(context)));

        Assert.Equal(EncryptionMessageText.ForParameter(EncryptionMessageText.InvalidPurpose, nameof(context)), exception.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void EnsureValid_NullPurpose_ThrowsArgumentException()
    {
        var context = new EncryptionContext(null!, Guid.NewGuid(), Guid.NewGuid());

        var exception = Assert.Throws<ArgumentException>(() => EnvelopeAssociatedData.EnsureValid(context, nameof(context)));

        Assert.Equal(EncryptionMessageText.ForParameter(EncryptionMessageText.InvalidPurpose, nameof(context)), exception.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void EnsureValid_EmptyItemId_ThrowsArgumentException()
    {
        var context = new EncryptionContext("secret.value", Guid.Empty, Guid.NewGuid());

        var exception = Assert.Throws<ArgumentException>(() => EnvelopeAssociatedData.EnsureValid(context, nameof(context)));

        Assert.Equal(EncryptionMessageText.ForParameter(EncryptionMessageText.EmptyContextId, nameof(context)), exception.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void EnsureValid_EmptyOwnerId_ThrowsArgumentException()
    {
        var context = new EncryptionContext("secret.value", Guid.NewGuid(), Guid.Empty);

        var exception = Assert.Throws<ArgumentException>(() => EnvelopeAssociatedData.EnsureValid(context, nameof(context)));

        Assert.Equal(EncryptionMessageText.ForParameter(EncryptionMessageText.EmptyContextId, nameof(context)), exception.Message);
    }

    [Fact]
    public void EnsureValid_NullContext_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => EnvelopeAssociatedData.EnsureValid(null!, "context"));

        Assert.Equal("context", exception.ParamName);
    }

    [Fact]
    public void Write_PurposeBytes_AreAscii()
    {
        var context = new EncryptionContext("secret.value", GoldenItem, GoldenOwner);

        var encoded = Encode(context);

        Assert.Equal("secret.value", Encoding.ASCII.GetString(encoded, 3, 12));
    }

    private static byte[] Encode(EncryptionContext context)
    {
        var buffer = new byte[EnvelopeAssociatedData.MaxLength];
        var written = EnvelopeAssociatedData.Write(EnvelopeFormat.Version1, context, buffer);
        return buffer[..written];
    }
}
