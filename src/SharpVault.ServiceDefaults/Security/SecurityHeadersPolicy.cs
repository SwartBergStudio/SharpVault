// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;

namespace SharpVault.ServiceDefaults.Security;

public sealed record SecurityHeadersPolicy(
    string ContentSecurityPolicy,
    IReadOnlyList<PathString> PathsWithoutContentSecurityPolicy)
{
    public static SecurityHeadersPolicy ForApi(bool allowApiReferenceUi)
    {
        IReadOnlyList<PathString> exempt = allowApiReferenceUi
            ? [new PathString(SecurityHeaderValues.ApiReferencePath)]
            : [];
        return new SecurityHeadersPolicy(SecurityHeaderValues.ApiContentSecurityPolicy, exempt);
    }

    public static SecurityHeadersPolicy ForWeb() =>
        new(SecurityHeaderValues.WebContentSecurityPolicy, []);
}
