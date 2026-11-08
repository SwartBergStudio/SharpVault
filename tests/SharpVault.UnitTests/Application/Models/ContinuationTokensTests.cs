// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.UnitTests.Application.Models;

public sealed class ContinuationTokensTests
{
    [Fact]
    public void ForSecret_RoundTripsThroughTryGetSecretId()
    {
        var id = Guid.NewGuid();

        var token = ContinuationTokens.ForSecret(id);

        Assert.True(ContinuationTokens.TryGetSecretId(token, out var parsed));
        Assert.Equal(id, parsed);
        Assert.True(ContinuationTokens.IsSecretToken(token));
        Assert.False(ContinuationTokens.IsRecycleBinToken(token));
    }

    [Fact]
    public void ForStorage_RoundTripsThroughTryGetStorageToken()
    {
        var token = ContinuationTokens.ForStorage("partition rowkey");

        Assert.True(ContinuationTokens.TryGetStorageToken(token, out var storage));
        Assert.Equal("partition rowkey", storage);
        Assert.True(ContinuationTokens.IsSecretToken(token));
        Assert.True(ContinuationTokens.IsRecycleBinToken(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("t!!!")]
    [InlineData("0123456789ABCDEF0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef\n")]
    [InlineData("01234567-89ab-cdef-0123-456789abcdef")]
    [InlineData("f_0123456789abcdef0123456789abcdef")]
    public void IsSecretToken_Malformed_IsFalse(string? token)
    {
        Assert.False(ContinuationTokens.IsSecretToken(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("x_0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789ABCDEF0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcde")]
    [InlineData("s_0123456789abcdef0123456789abcdef\n")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void IsRecycleBinToken_Malformed_IsFalse(string? token)
    {
        Assert.False(ContinuationTokens.IsRecycleBinToken(token));
    }

    [Theory]
    [InlineData("f_0123456789abcdef0123456789abcdef")]
    [InlineData("s_0123456789abcdef0123456789abcdef")]
    public void IsRecycleBinToken_Key_IsTrue(string token)
    {
        Assert.True(ContinuationTokens.IsRecycleBinToken(token));
        Assert.True(ContinuationTokens.TryGetBinKey(token, out var key));
        Assert.Equal(token, key);
    }

    [Theory]
    [InlineData("xs_0123456789abcdef0123456789abcdef")]
    [InlineData("f_0123456789abcdef0123456789abcdefs_0123456789abcdef0123456789abcdef")]
    [InlineData("' or RowKey ne 's_0123456789abcdef0123456789abcdef")]
    public void IsRecycleBinToken_KeyWithLeadingText_IsFalse(string token)
    {
        Assert.False(ContinuationTokens.IsRecycleBinToken(token));
        Assert.False(ContinuationTokens.TryGetBinKey(token, out _));
    }

    [Theory]
    [InlineData("x0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void IsSecretToken_IdWithLeadingOrExtraText_IsFalse(string token)
    {
        Assert.False(ContinuationTokens.IsSecretToken(token));
        Assert.False(ContinuationTokens.TryGetSecretId(token, out _));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("")]
    public void TryGetStorageToken_PayloadWithoutTheStoragePrefix_IsFalse(string prefix)
    {
        var payload = System.Buffers.Text.Base64Url.EncodeToString("some storage token"u8.ToArray());

        Assert.False(ContinuationTokens.TryGetStorageToken(prefix + payload, out _));
        Assert.False(ContinuationTokens.IsSecretToken(prefix + payload));
        Assert.False(ContinuationTokens.IsRecycleBinToken(prefix + payload));
    }

    [Fact]
    public void TryGetStorageToken_OverMaxLength_IsFalse()
    {
        var token = ContinuationTokens.ForStorage(new string('x', ContinuationTokens.MaxLength));

        Assert.True(token.Length > ContinuationTokens.MaxLength);
        Assert.False(ContinuationTokens.TryGetStorageToken(token, out _));
    }

    [Fact]
    public void TryGetStorageToken_InvalidUtf8_IsFalse()
    {
        var token = "t" + System.Buffers.Text.Base64Url.EncodeToString(new byte[] { 0xFF, 0xFE, 0xFD });

        Assert.False(ContinuationTokens.TryGetStorageToken(token, out _));
    }
}
