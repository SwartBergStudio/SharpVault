// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class VaultStatusTextTests
{
    [Fact]
    public void For_Invalid_ReturnsTheDetail()
    {
        Assert.Equal("The name is too long.", VaultStatusText.For(ApiResponse<string>.Invalid("The name is too long.")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void For_InvalidWithoutDetail_ReturnsGenericText(string? detail)
    {
        Assert.Equal("Check the fields and try again.", VaultStatusText.For(ApiResponse<string>.Invalid(detail)));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(ApiCallStatus.Failed)]
    [InlineData(ApiCallStatus.SignInRequired)]
    public void For_NonInvalidStatusWithDetail_NeverShowsTheDetail(ApiCallStatus status)
    {
        var response = new ApiResponse<string>(status, null, "server detail with internal id 4711");

        Assert.DoesNotContain("4711", VaultStatusText.For(response), StringComparison.Ordinal);
    }
}
