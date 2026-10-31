// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;

namespace SharpVault.Web.Api;

public sealed class SharpVaultApiOptionsValidator : IValidateOptions<SharpVaultApiOptions>
{
    internal const string BaseAddressInvalid = "SharpVaultApi:BaseAddress must be an absolute URI.";
    internal const string ScopesRequired = "SharpVaultApi:Scopes must contain at least one scope.";

    public ValidateOptionsResult Validate(string? name, SharpVaultApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        if (options.BaseAddress is null || !options.BaseAddress.IsAbsoluteUri)
        {
            failures.Add(BaseAddressInvalid);
        }

        if (options.Scopes is null || options.Scopes.Length == 0 || options.Scopes.Any(string.IsNullOrWhiteSpace))
        {
            failures.Add(ScopesRequired);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
