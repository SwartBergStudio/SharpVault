// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;

namespace SharpVault.Benchmarks.Support;

internal static class BenchmarkEncryption
{
    private const int MaterialBytes = 36;

    public static EnvelopeEncryptor CreateDevelopmentEncryptor()
    {
        var material = Convert.ToBase64String(RandomNumberGenerator.GetBytes(MaterialBytes));
        var options = Options.Create(new EncryptionOptions { DevelopmentKeyMaterial = material });
        var provider = new DevelopmentKeyWrapProvider(new BenchmarkHostEnvironment(), options);
        return new EnvelopeEncryptor(provider, NullLogger<EnvelopeEncryptor>.Instance);
    }
}
