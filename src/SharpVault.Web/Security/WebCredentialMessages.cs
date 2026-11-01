// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web.Security;

internal static class WebCredentialMessages
{
    public const string ClientSecretNotAllowed = "Outside the Development environment the web app must authenticate to Entra ID with a managed identity federated credential or a certificate. Client secrets are not allowed.";

    public const string CredentialRequired = "Outside the Development environment AzureAd:ClientCredentials must contain at least one managed identity or certificate credential.";
}
