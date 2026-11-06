// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.WebTests;

public sealed class JwtShapeTests
{
    [Fact]
    public void ContainsJwt_RealToken_IsDetected()
    {
        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);

        Assert.True(WebTestSupport.ContainsJwt($"<input value=\"{token}\">"));
    }

    [Fact]
    public void ContainsJwt_TokenWithoutTheSignature_IsDetected()
    {
        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);

        Assert.True(WebTestSupport.ContainsJwt(string.Join('.', token.Split('.').Take(2)) + "."));
    }

    [Theory]
    [InlineData("CfDJ8abcdeyJabcdefghij1234567890-_abcdef")]
    [InlineData("eyJ")]
    [InlineData("eyJabcdefghij")]
    [InlineData("plain text with no token")]
    public void ContainsJwt_RandomBase64WithTheMagicPrefix_IsNotDetected(string text)
    {
        Assert.False(WebTestSupport.ContainsJwt(text));
    }
}
