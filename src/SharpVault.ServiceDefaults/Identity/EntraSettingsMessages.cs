// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.ServiceDefaults.Identity;

internal static class EntraSettingsMessages
{
    public const string InstanceInvalid = "AzureAd:Instance must be an absolute https URI with no path, query or user information.";
    public const string TenantIdInvalid = "AzureAd:TenantId must be the directory (tenant) id GUID. Multi-tenant values such as common, organizations and consumers are not allowed.";
    public const string ClientIdInvalid = "AzureAd:ClientId must be the application (client) id GUID.";
}
