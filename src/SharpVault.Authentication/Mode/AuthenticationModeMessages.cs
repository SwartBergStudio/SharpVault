// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Authentication.Mode;

internal static class AuthenticationModeMessages
{
    public const string TenantIdKey = "AzureAd:TenantId";
    public const string ClientIdKey = "AzureAd:ClientId";
    public const string TenantIdMissing = "AzureAd:TenantId is required when AzureAd:ClientId is set.";
    public const string ClientIdMissing = "AzureAd:ClientId is required when AzureAd:TenantId is set.";
    public const string EntraRequiredOutsideDevelopment = "AzureAd:TenantId and AzureAd:ClientId are required outside the Development environment.";
}
