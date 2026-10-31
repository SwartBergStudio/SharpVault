// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.Infrastructure.Encryption.KeyVault;

namespace SharpVault.Infrastructure.Encryption;

public static class EncryptionRegistration
{
    public static IHostApplicationBuilder AddEncryption(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var section = builder.Configuration.GetSection(EncryptionOptions.SectionName);
        var options = section.Get<EncryptionOptions>() ?? new EncryptionOptions();
        var kind = EncryptionSettingsGuard.ResolveProvider(options, builder.Environment);

        builder.Services.AddOptions<EncryptionOptions>().Bind(section).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();

        if (kind == KeyWrapProviderKind.Development)
        {
            builder.Services.AddSingleton<IKeyWrapProvider, DevelopmentKeyWrapProvider>();
        }
        else
        {
            builder.Services.AddSingleton<IKeyVaultCryptographyClientFactory>(_ =>
                new KeyVaultCryptographyClientFactory(new DefaultAzureCredential(), KeyVaultCryptographyClientFactory.CreateOptions()));
            builder.Services.AddSingleton<IKeyWrapProvider, KeyVaultKeyWrapProvider>();
        }

        builder.Services.AddSingleton<IEnvelopeEncryptor, EnvelopeEncryptor>();
        builder.Services.AddHostedService<KeyWrapProviderStartupCheck>();
        return builder;
    }
}
