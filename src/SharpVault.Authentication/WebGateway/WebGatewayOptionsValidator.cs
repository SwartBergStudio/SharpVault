// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;

namespace SharpVault.Authentication.WebGateway;

public sealed class WebGatewayOptionsValidator(bool keyRequired) : IValidateOptions<WebGatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, WebGatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var key = options.NormalizedKey;
        if (key is null)
        {
            return keyRequired ? ValidateOptionsResult.Fail(WebGatewayMessages.KeyRequired) : ValidateOptionsResult.Success;
        }

        return key.Length < WebGatewayOptions.MinimumKeyLength
            ? ValidateOptionsResult.Fail(WebGatewayMessages.KeyTooShort)
            : ValidateOptionsResult.Success;
    }
}
