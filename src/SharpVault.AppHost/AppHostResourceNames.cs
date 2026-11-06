// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.AppHost;

public static class AppHostResourceNames
{
    public const string Storage = "storage";
    public const string Tables = "tables";
    public const string Api = "api";
    public const string Web = "web";
    public const string HealthPath = "/health";
    public const string DevelopmentKeyMaterial = "development-key-material";
    public const string DevelopmentKeyMaterialVariable = "Encryption__DevelopmentKeyMaterial";
    public const int DevelopmentKeyMaterialLength = 64;
    public const string TenantIdKey = "AzureAd:TenantId";
    public const string InstanceKey = "AzureAd:Instance";
    public const string ApiClientIdKey = "AzureAd:Api:ClientId";
    public const string WebClientIdKey = "AzureAd:Web:ClientId";
    public const string WebClientSecretKey = "AzureAd:Web:ClientSecret";
    public const string WebClientSecretParameter = "azuread-web-client-secret";
    public const string TenantIdVariable = "AzureAd__TenantId";
    public const string InstanceVariable = "AzureAd__Instance";
    public const string ClientIdVariable = "AzureAd__ClientId";
    public const string ClientCredentialSourceTypeVariable = "AzureAd__ClientCredentials__0__SourceType";
    public const string ClientCredentialSecretVariable = "AzureAd__ClientCredentials__0__ClientSecret";
    public const string ClientSecretSourceType = "ClientSecret";
    public const string ApiScopeVariable = "SharpVaultApi__Scopes__0";
    public const string ApiScopePrefix = "api://";
    public const string ApiScopeSuffix = "/access_as_user";
}
