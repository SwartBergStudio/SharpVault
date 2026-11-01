// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Web.Security;

public static class WebCredentialGuard
{
    private const string ClientSecretKey = "ClientSecret";
    private const string ClientCredentialsKey = "ClientCredentials";
    private const string SourceTypeKey = "SourceType";
    private const string ClientSecretSourceType = "ClientSecret";

    private static readonly string[] AllowedSourceTypes =
    [
        "SignedAssertionFromManagedIdentity",
        "KeyVault",
        "StoreWithThumbprint",
        "StoreWithDistinguishedName",
    ];

    public static void EnsureAllowed(IConfiguration azureAdSection, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(azureAdSection);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsDevelopment())
        {
            return;
        }

        var credentials = azureAdSection.GetSection(ClientCredentialsKey).GetChildren().ToList();

        if (!string.IsNullOrWhiteSpace(azureAdSection[ClientSecretKey]) || credentials.Any(IsClientSecret))
        {
            throw new InvalidOperationException(WebCredentialMessages.ClientSecretNotAllowed);
        }

        if (!credentials.Any(IsAllowedSource))
        {
            throw new InvalidOperationException(WebCredentialMessages.CredentialRequired);
        }
    }

    private static bool IsClientSecret(IConfigurationSection credential) =>
        string.Equals(credential[SourceTypeKey], ClientSecretSourceType, StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(credential[ClientSecretKey]);

    private static bool IsAllowedSource(IConfigurationSection credential) =>
        AllowedSourceTypes.Contains(credential[SourceTypeKey], StringComparer.OrdinalIgnoreCase);
}
