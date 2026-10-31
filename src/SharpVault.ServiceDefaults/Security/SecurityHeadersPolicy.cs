// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace SharpVault.ServiceDefaults.Security;

public sealed record SecurityHeadersPolicy(
    string ContentSecurityPolicy,
    IReadOnlyList<PathString> PathsWithoutContentSecurityPolicy)
{
    private static readonly CompositeFormat WebContentSecurityPolicyTemplate =
        CompositeFormat.Parse(SecurityHeaderValues.WebContentSecurityPolicyFormat);

    public static SecurityHeadersPolicy ForApi(bool allowApiReferenceUi)
    {
        IReadOnlyList<PathString> exempt = allowApiReferenceUi
            ? [new PathString(SecurityHeaderValues.ApiReferencePath)]
            : [];
        return new SecurityHeadersPolicy(SecurityHeaderValues.ApiContentSecurityPolicy, exempt);
    }

    public static SecurityHeadersPolicy ForWeb(Uri identityProviderOrigin)
    {
        ArgumentNullException.ThrowIfNull(identityProviderOrigin);
        if (!identityProviderOrigin.IsAbsoluteUri || identityProviderOrigin.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(SecurityHeaderValues.IdentityProviderOriginInvalid, nameof(identityProviderOrigin));
        }

        var contentSecurityPolicy = string.Format(
            CultureInfo.InvariantCulture,
            WebContentSecurityPolicyTemplate,
            identityProviderOrigin.GetLeftPart(UriPartial.Authority));
        return new SecurityHeadersPolicy(contentSecurityPolicy, []);
    }
}
