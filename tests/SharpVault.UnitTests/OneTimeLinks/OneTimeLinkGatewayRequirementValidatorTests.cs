// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using SharpVault.Api.Security;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.UnitTests.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkGatewayRequirementValidatorTests
{
    private const string ShortKey = "short-secret-value";
    private const string ValidKey = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void Enabled_KeyMissing_Fails()
    {
        var result = Validator(null).Validate(null, new OneTimeLinkOptions { Enabled = true });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Enabled_KeyUnder32Characters_Fails()
    {
        var result = Validator(new string('k', 31)).Validate(null, new OneTimeLinkOptions { Enabled = true });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Disabled_KeyMissing_Passes()
    {
        var result = Validator(null).Validate(null, new OneTimeLinkOptions { Enabled = false });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Failure_MessageNeverEchoesTheKey()
    {
        var result = Validator(ShortKey).Validate(null, new OneTimeLinkOptions { Enabled = true });

        Assert.True(result.Failed);
        Assert.DoesNotContain(ShortKey, string.Join(" ", result.Failures), StringComparison.Ordinal);
        Assert.Contains(LinkGatewayMessages.LinksNeedGatewayKey, result.Failures);
    }

    [Fact]
    public void Enabled_KeyOf32Characters_Passes()
    {
        var result = Validator(ValidKey).Validate(null, new OneTimeLinkOptions { Enabled = true });

        Assert.True(result.Succeeded);
    }

    private static OneTimeLinkGatewayRequirementValidator Validator(string? key) =>
        new(Options.Create(new WebGatewayOptions { Key = key }));
}
