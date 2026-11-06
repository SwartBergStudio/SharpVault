// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Services.Clients;

internal static class ApiRouteTemplate
{
    public static string Bind(this string template, string parameterName, Guid value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

        var placeholder = "{" + parameterName + ":guid}";
        if (!template.Contains(placeholder, StringComparison.Ordinal))
        {
            throw new ArgumentException("The route template has no such placeholder.", nameof(parameterName));
        }

        return template.Replace(placeholder, value.ToString("D"), StringComparison.Ordinal);
    }
}
