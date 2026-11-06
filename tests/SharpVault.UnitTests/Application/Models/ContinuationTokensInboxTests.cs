// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.UnitTests.Application.Models;

public sealed class ContinuationTokensInboxTests
{
    private const string Owner = "0123456789abcdef0123456789abcdef";
    private const string Item = "fedcba9876543210fedcba9876543210";

    [Theory]
    [InlineData("s")]
    [InlineData("f")]
    public void ForInbox_ValidRowKey_RoundTripsThroughTryGetInboxKey(string kind)
    {
        var rowKey = $"{Owner}_{kind}_{Item}";

        var token = ContinuationTokens.ForInbox(rowKey);

        Assert.Equal(rowKey, token);
        Assert.True(ContinuationTokens.IsInboxToken(token));
        Assert.True(ContinuationTokens.TryGetInboxKey(token, out var key));
        Assert.Equal(rowKey, key);
    }

    [Fact]
    public void ForStorage_Token_IsAnInboxToken()
    {
        var token = ContinuationTokens.ForStorage("next-3");

        Assert.True(ContinuationTokens.IsInboxToken(token));
        Assert.False(ContinuationTokens.TryGetInboxKey(token, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("x_0123456789abcdef0123456789abcdef_s_fedcba9876543210fedcba9876543210")]
    [InlineData("0123456789abcdef0123456789abcdef_x_fedcba9876543210fedcba9876543210")]
    [InlineData("0123456789abcdef0123456789abcdef_S_fedcba9876543210fedcba9876543210")]
    [InlineData("0123456789ABCDEF0123456789abcdef_s_fedcba9876543210fedcba9876543210")]
    [InlineData("0123456789abcdef0123456789abcdef_s_fedcba9876543210fedcba987654321")]
    [InlineData("0123456789abcdef0123456789abcde_s_fedcba9876543210fedcba9876543210")]
    [InlineData("0123456789abcdef0123456789abcdef_s_fedcba9876543210fedcba9876543210\n")]
    [InlineData("\n0123456789abcdef0123456789abcdef_s_fedcba9876543210fedcba9876543210")]
    [InlineData(" 0123456789abcdef0123456789abcdef_s_fedcba9876543210fedcba9876543210")]
    [InlineData("0123456789abcdef0123456789abcdef_s_fedcba9876543210fedcba9876543210 ")]
    [InlineData("0123456789abcdef0123456789abcdef_s_fedcba9876543210fedcba9876543210_s_fedcba9876543210fedcba9876543210")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef_s_fedcba9876543210fedcba9876543210")]
    [InlineData("' or PartitionKey ne '")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef__fedcba9876543210fedcba9876543210")]
    public void IsInboxToken_Malformed_IsFalse(string? token)
    {
        Assert.False(ContinuationTokens.IsInboxToken(token));
        if (token is not null)
        {
            Assert.False(ContinuationTokens.TryGetInboxKey(token, out _));
        }
    }

    [Fact]
    public void TryGetInboxKey_OverMaxLength_IsFalse()
    {
        var token = $"{Owner}_s_{Item}" + new string('a', ContinuationTokens.MaxLength);

        Assert.False(ContinuationTokens.TryGetInboxKey(token, out _));
    }

    [Fact]
    public void ForInbox_EmptyOrNull_Throws()
    {
        Assert.Throws<ArgumentException>(() => ContinuationTokens.ForInbox(string.Empty));
        Assert.Throws<ArgumentNullException>(() => ContinuationTokens.ForInbox(null!));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef_s_fedcba9876543210fedcba9876543210")]
    [InlineData("0123456789abcdef0123456789abcdef_f_fedcba9876543210fedcba9876543210")]
    public void InboxKey_IsNotAcceptedAsASecretOrRecycleBinToken(string token)
    {
        Assert.False(ContinuationTokens.IsSecretToken(token));
        Assert.False(ContinuationTokens.IsRecycleBinToken(token));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    [InlineData("f_0123456789abcdef0123456789abcdef")]
    public void SecretAndBinKeys_AreNotAcceptedAsInboxTokens(string token)
    {
        Assert.False(ContinuationTokens.IsInboxToken(token));
    }
}
