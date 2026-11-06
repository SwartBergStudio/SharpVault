// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharpVault.E2ETests.Hosting;

public static class E2ECertificate
{
    public const string FileName = "e2e-localhost.pfx";
    public const string DnsName = "localhost";
    public const int KeySizeBits = 2048;
    public const int ValidDays = 7;
    public const int PasswordBytes = 24;

    public static string Create(string directory, string password)
    {
        using var key = RSA.Create(KeySizeBits);
        var request = new CertificateRequest($"CN={DnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(DnsName);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(ValidDays));
        var path = Path.Combine(directory, FileName);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }

    public static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(PasswordBytes));
}
