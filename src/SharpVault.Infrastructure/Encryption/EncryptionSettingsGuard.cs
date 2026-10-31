// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using SharpVault.Infrastructure.Encryption.KeyVault;

namespace SharpVault.Infrastructure.Encryption;

public static class EncryptionSettingsGuard
{
    public const int MinimumDevelopmentKeyMaterialLength = 32;

    public static bool TryResolveProvider(
        EncryptionOptions options,
        IHostEnvironment environment,
        out KeyWrapProviderKind kind,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var isDevelopment = environment.IsDevelopment();
        kind = options.Provider ?? (isDevelopment ? KeyWrapProviderKind.Development : KeyWrapProviderKind.KeyVault);

        if (options.Provider is { } configured && !Enum.IsDefined(configured))
        {
            error = EncryptionMessages.UnknownProvider;
            return false;
        }

        if (!isDevelopment && options.Provider == KeyWrapProviderKind.Development)
        {
            error = EncryptionMessages.DevelopmentProviderNotAllowed;
            return false;
        }

        if (!isDevelopment && !string.IsNullOrEmpty(options.DevelopmentKeyMaterial))
        {
            error = EncryptionMessages.DevelopmentKeyMaterialNotAllowed;
            return false;
        }

        if (kind == KeyWrapProviderKind.KeyVault && !KeyVaultKeyIds.TryParseConfigured(options.KeyVaultKeyId, out _))
        {
            error = EncryptionMessages.KeyVaultKeyIdInvalid;
            return false;
        }

        if (kind == KeyWrapProviderKind.Development
            && (options.DevelopmentKeyMaterial?.Trim().Length ?? 0) < MinimumDevelopmentKeyMaterialLength)
        {
            error = EncryptionMessages.DevelopmentKeyMaterialMissing;
            return false;
        }

        error = null;
        return true;
    }

    public static KeyWrapProviderKind ResolveProvider(EncryptionOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        if (!TryResolveProvider(options, environment, out var kind, out var error))
        {
            throw new InvalidOperationException(error);
        }

        return kind;
    }
}
