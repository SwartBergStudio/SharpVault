// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class VaultStatusTextTests
{
    [Theory]
    [InlineData(ApiCallStatus.NotFound, "This item no longer exists.")]
    [InlineData(ApiCallStatus.RateLimited, "Too many requests. Try again in a minute.")]
    [InlineData(ApiCallStatus.Conflict, "This item was changed elsewhere. Reload and try again.")]
    [InlineData(ApiCallStatus.ConfigurationMismatch, "The web app and the API are set up for different sign-in modes. Check that both run in the same mode.")]
    [InlineData(ApiCallStatus.Failed, "That did not work. Try again.")]
    [InlineData(ApiCallStatus.SignInRequired, "That did not work. Try again.")]
    [InlineData(ApiCallStatus.AccessDenied, "That did not work. Try again.")]
    [InlineData(ApiCallStatus.Success, "That did not work. Try again.")]
    public void For_EachStatus_ReturnsExpectedText(ApiCallStatus status, string expected)
    {
        var response = new ApiResponse<string>(status, null);

        Assert.Equal(expected, VaultStatusText.For(response));
    }

    [Fact]
    public void For_Invalid_ReturnsTheDetail()
    {
        Assert.Equal("The name is too long.", VaultStatusText.For(ApiResponse<string>.Invalid("The name is too long.")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void For_InvalidWithoutDetail_ReturnsGenericText(string? detail)
    {
        Assert.Equal("Check the fields and try again.", VaultStatusText.For(ApiResponse<string>.Invalid(detail)));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(ApiCallStatus.NotFound)]
    [InlineData(ApiCallStatus.Conflict)]
    [InlineData(ApiCallStatus.RateLimited)]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.AccessDenied)]
    [InlineData(ApiCallStatus.SignInRequired)]
    [InlineData(ApiCallStatus.ConfigurationMismatch)]
    public void For_NonInvalidStatusWithDetail_NeverShowsTheDetail(ApiCallStatus status)
    {
        var response = new ApiResponse<string>(status, null, "server detail with internal id 4711");

        Assert.DoesNotContain("4711", VaultStatusText.For(response), StringComparison.Ordinal);
    }

    [Fact]
    public void For_Constants_MatchTheLoadablePageTexts()
    {
        Assert.Equal(LoadableComponentBase<string>.NotFoundText, VaultStatusText.For(ApiResponse<string>.NotFound()));
        Assert.Equal(LoadableComponentBase<string>.RateLimitedText, VaultStatusText.For(ApiResponse<string>.RateLimited()));
    }

    [Fact]
    public void For_NullResponse_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VaultStatusText.For<string>(null!));
    }
}
