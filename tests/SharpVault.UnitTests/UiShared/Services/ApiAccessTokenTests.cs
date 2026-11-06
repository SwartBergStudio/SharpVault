// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class ApiAccessTokenTests
{
    [Fact]
    public void Bearer_Value_CarriesTheTokenAndBearerKind()
    {
        var token = ApiAccessToken.Bearer("abc");

        Assert.Equal(ApiAccessTokenKind.Bearer, token.Kind);
        Assert.Equal("abc", token.Value);
    }

    [Fact]
    public void NoToken_Singleton_HasNoValue()
    {
        Assert.Equal(ApiAccessTokenKind.None, ApiAccessToken.NoToken.Kind);
        Assert.Null(ApiAccessToken.NoToken.Value);
    }

    [Fact]
    public void SignInRequired_Singleton_HasNoValue()
    {
        Assert.Equal(ApiAccessTokenKind.SignInRequired, ApiAccessToken.SignInRequired.Kind);
        Assert.Null(ApiAccessToken.SignInRequired.Value);
    }

    [Fact]
    public void Kinds_AreDistinct()
    {
        Assert.NotEqual(ApiAccessToken.NoToken, ApiAccessToken.SignInRequired);
        Assert.NotEqual(ApiAccessToken.NoToken, ApiAccessToken.Bearer("abc"));
    }
}
