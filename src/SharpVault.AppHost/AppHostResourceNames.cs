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
}
