// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Configuration;

namespace SharpVault.ServiceDefaults.Identity;

public sealed record EntraApplicationSettings(Uri Instance, Guid TenantId, Guid ClientId)
{
    public const string SectionName = "AzureAd";

    private const string GuidFormat = "D";
    private const string RootPath = "/";

    public string V2Issuer => $"{Instance.GetLeftPart(UriPartial.Authority)}/{TenantId:D}/v2.0";

    public string V1Issuer => $"https://sts.windows.net/{TenantId:D}/";

    public Uri IdentityProviderOrigin => new(Instance.GetLeftPart(UriPartial.Authority));

    public static EntraApplicationSettings Read(IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var instance = ReadInstance(section["Instance"]);
        var tenantId = ReadGuid(section["TenantId"], EntraSettingsMessages.TenantIdInvalid);
        var clientId = ReadGuid(section["ClientId"], EntraSettingsMessages.ClientIdInvalid);
        return new EntraApplicationSettings(instance, tenantId, clientId);
    }

    private static Uri ReadInstance(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var instance)
            || instance.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(instance.UserInfo)
            || !string.IsNullOrEmpty(instance.Query)
            || !string.IsNullOrEmpty(instance.Fragment)
            || !string.Equals(instance.AbsolutePath, RootPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(EntraSettingsMessages.InstanceInvalid);
        }

        return instance;
    }

    private static Guid ReadGuid(string? value, string message)
    {
        if (!Guid.TryParseExact(value, GuidFormat, out var id) || id == Guid.Empty)
        {
            throw new InvalidOperationException(message);
        }

        return id;
    }
}
