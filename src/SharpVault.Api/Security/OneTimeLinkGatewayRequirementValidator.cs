// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.Api.Security;

public sealed class OneTimeLinkGatewayRequirementValidator(IOptions<WebGatewayOptions> gateway) : IValidateOptions<OneTimeLinkOptions>
{
    public ValidateOptionsResult Validate(string? name, OneTimeLinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var key = gateway.Value.NormalizedKey;
        return key is not null && key.Length >= WebGatewayOptions.MinimumKeyLength
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(LinkGatewayMessages.LinksNeedGatewayKey);
    }
}
