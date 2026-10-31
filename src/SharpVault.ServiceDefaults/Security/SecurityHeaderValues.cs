// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.ServiceDefaults.Security;

public static class SecurityHeaderValues
{
    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    public const string WebContentSecurityPolicyFormat = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' {0}; upgrade-insecure-requests";
    public const string IdentityProviderOriginInvalid = "The identity provider origin must be an absolute https URI.";
    public const string NoSniff = "nosniff";
    public const string FrameDeny = "DENY";
    public const string NoReferrer = "no-referrer";
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";
    public const string PermissionsPolicy = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
    public const string SameOrigin = "same-origin";
    public const string NoStore = "no-store";
    public const string ApiReferencePath = "/scalar";
}
