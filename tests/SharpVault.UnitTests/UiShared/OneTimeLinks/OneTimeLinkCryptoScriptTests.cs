// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.OneTimeLinks;

[Trait("Category", "Security")]
public sealed class OneTimeLinkCryptoScriptTests
{
    private const string Passphrase = "correct horse battery";
    private const string Marker = "PLAINTEXT-MARKER-7f3a";
    private const int KeyBytes = 32;
    private const int IvBytes = 12;
    private const int SaltBytes = 16;
    private const int TagBytes = 16;
    private const int PassphraseIterations = 600000;
    private const byte PlainVersion = 0x80;
    private const byte PassphraseVersion = 0x81;
    private const int PlainIvOffset = 1;
    private const int PlainCiphertextOffset = 1 + IvBytes;
    private const int PassphraseSaltOffset = 1;
    private const int PassphraseIterationsOffset = 1 + SaltBytes;
    private const int PassphraseIvOffset = PassphraseIterationsOffset + 4;
    private const int PassphraseCiphertextOffset = PassphraseIvOffset + IvBytes;
    private const string PlainInfo = "sharpvault.otl.v1.plain";
    private const string PassphraseInfo = "sharpvault.otl.v1.passphrase";

    private static readonly string ModulePath = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared", "wwwroot", "js", "one-time-link-crypto.js");
    private static readonly string HarnessPath = Path.Combine(RepositoryRoot.Path, "tests", "SharpVault.UnitTests", "UiShared", "OneTimeLinks", "one-time-link-crypto.harness.mjs");
    private static readonly JsonSerializerOptions ResultOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Seal_ThenOpen_NoPassphrase_ReturnsThePlaintext()
    {
        var linked = await SealAsync("{\"name\":\"plain\"}");

        var opened = await OpenAsync(linked.Payload, linked.Fragment);

        Assert.Equal("{\"name\":\"plain\"}", opened.Plaintext);
    }

    [Fact]
    public async Task Seal_ThenOpen_Passphrase_ReturnsThePlaintext()
    {
        var linked = await SealAsync("{\"name\":\"guarded\"}", Passphrase);

        var opened = await OpenAsync(linked.Payload, linked.Fragment, Passphrase);

        Assert.Equal("{\"name\":\"guarded\"}", opened.Plaintext);
    }

    [Fact]
    public async Task Seal_UnicodeAndMaximumSizeSnapshot_RoundTrips()
    {
        var plaintext = string.Concat(Enumerable.Repeat("é€", 18000)) + new string('a', 20000);
        Assert.True(Encoding.UTF8.GetByteCount(plaintext) >= 110000);
        var linked = await SealAsync(plaintext);

        var opened = await OpenAsync(linked.Payload, linked.Fragment);

        Assert.Equal(plaintext, opened.Plaintext);
    }

    [Fact]
    public async Task Open_BlobSealedByDotNetAesGcmHkdf_ReturnsThePlaintext()
    {
        var linkKey = RandomNumberGenerator.GetBytes(KeyBytes);
        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        var header = Concat([PlainVersion], iv);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, linkKey, KeyBytes, [], Encoding.UTF8.GetBytes(PlainInfo));
        var payload = Concat(header, Encrypt(aesKey, iv, header, Encoding.UTF8.GetBytes("{\"name\":\"dotnet\"}")));

        var opened = await OpenAsync(payload, Base64Url.EncodeToString(linkKey));

        Assert.Equal("{\"name\":\"dotnet\"}", opened.Plaintext);
    }

    [Fact]
    public async Task Open_BlobSealedByDotNetWithPassphrase_ReturnsThePlaintext()
    {
        var linkKey = RandomNumberGenerator.GetBytes(KeyBytes);
        var payload = SealPassphraseInDotNet(linkKey, Passphrase, "{\"name\":\"dotnet-guarded\"}");

        var opened = await OpenAsync(payload, PassphraseFragment(linkKey), Passphrase);

        Assert.Equal("{\"name\":\"dotnet-guarded\"}", opened.Plaintext);
    }

    [Fact]
    public async Task Open_WrongKey_FailsAuthentication()
    {
        var linked = await SealAsync("{\"name\":\"key\"}");
        var other = await SealAsync("{\"name\":\"other\"}");

        var opened = await OpenAsync(linked.Payload, other.Fragment);

        Assert.Equal("authentication-failed", opened.Error);
        Assert.Null(opened.Plaintext);
    }

    [Fact]
    public async Task Open_WrongPassphrase_FailsAuthentication()
    {
        var linked = await SealAsync("{\"name\":\"pass\"}", Passphrase);

        var opened = await OpenAsync(linked.Payload, linked.Fragment, "a different passphrase");

        Assert.Equal("authentication-failed", opened.Error);
        Assert.Null(opened.Plaintext);
    }

    [Fact]
    public async Task Open_PassphraseBlobWithNoPassphrase_ReportsPassphraseRequired()
    {
        var linked = await SealAsync("{\"name\":\"pass\"}", Passphrase);

        var opened = await OpenAsync(linked.Payload, linked.Fragment);

        Assert.Equal("passphrase-required", opened.Error);
    }

    [Fact]
    public async Task Open_TamperedCiphertext_FailsAuthentication()
    {
        var linked = await SealAsync("{\"name\":\"ct\"}");
        var payload = Tamper(linked.Payload, PlainCiphertextOffset);

        var opened = await OpenAsync(payload, linked.Fragment);

        Assert.Equal("authentication-failed", opened.Error);
    }

    [Fact]
    public async Task Open_TamperedTag_FailsAuthentication()
    {
        var linked = await SealAsync("{\"name\":\"tag\"}");
        var payload = Tamper(linked.Payload, linked.Payload.Length - 1);

        var opened = await OpenAsync(payload, linked.Fragment);

        Assert.Equal("authentication-failed", opened.Error);
    }

    [Fact]
    public async Task Open_TamperedIv_FailsAuthentication()
    {
        var linked = await SealAsync("{\"name\":\"iv\"}");
        var payload = Tamper(linked.Payload, PlainIvOffset);

        var opened = await OpenAsync(payload, linked.Fragment);

        Assert.Equal("authentication-failed", opened.Error);
    }

    [Fact]
    public async Task Open_TamperedVersionByte_Fails()
    {
        var linked = await SealAsync("{\"name\":\"version\"}");
        var payload = (byte[])linked.Payload.Clone();
        payload[0] = PassphraseVersion;

        var opened = await OpenAsync(payload, linked.Fragment);

        Assert.Null(opened.Plaintext);
        Assert.NotNull(opened.Error);
    }

    [Fact]
    public async Task Open_TamperedSalt_FailsAuthentication()
    {
        var linked = await SealAsync("{\"name\":\"salt\"}", Passphrase);
        var payload = Tamper(linked.Payload, PassphraseSaltOffset);

        var opened = await OpenAsync(payload, linked.Fragment, Passphrase);

        Assert.Equal("authentication-failed", opened.Error);
    }

    [Fact]
    public async Task Open_ChangedIterationsInRange_FailsAuthentication()
    {
        var linked = await SealAsync("{\"name\":\"iterations\"}", Passphrase);
        var payload = (byte[])linked.Payload.Clone();
        var changed = BitConverter.GetBytes((uint)(PassphraseIterations + 1));
        Array.Reverse(changed);
        changed.CopyTo(payload, PassphraseIterationsOffset);

        var opened = await OpenAsync(payload, linked.Fragment, Passphrase);

        Assert.Equal("authentication-failed", opened.Error);
    }

    [Fact]
    public async Task Open_IterationsOutOfRange_IsMalformed()
    {
        var linked = await SealAsync("{\"name\":\"range\"}", Passphrase);
        var payload = (byte[])linked.Payload.Clone();
        var tooFew = BitConverter.GetBytes((uint)99999);
        Array.Reverse(tooFew);
        tooFew.CopyTo(payload, PassphraseIterationsOffset);

        var opened = await OpenAsync(payload, linked.Fragment, Passphrase);

        Assert.Equal("malformed", opened.Error);
    }

    [Fact]
    public async Task Open_TruncatedBlob_IsMalformed()
    {
        var linked = await SealAsync("{\"name\":\"short\"}");
        var payload = linked.Payload[..29];

        var opened = await OpenAsync(payload, linked.Fragment);

        Assert.Equal("malformed", opened.Error);
    }

    [Fact]
    public async Task Open_ServerVersionByte0x01_IsUnsupported()
    {
        var linked = await SealAsync("{\"name\":\"v1\"}");
        var payload = (byte[])linked.Payload.Clone();
        payload[0] = 0x01;

        var opened = await OpenAsync(payload, linked.Fragment);

        Assert.Equal("unsupported-version", opened.Error);
    }

    [Fact]
    public async Task Seal_TwoCalls_UseDifferentKeysIvsAndSalts()
    {
        var first = await SealAsync("{\"name\":\"same\"}");
        var second = await SealAsync("{\"name\":\"same\"}");

        Assert.NotEqual(first.Fragment, second.Fragment);
        Assert.NotEqual(first.Payload.AsSpan(PlainIvOffset, IvBytes).ToArray(), second.Payload.AsSpan(PlainIvOffset, IvBytes).ToArray());
        Assert.NotEqual(first.Payload, second.Payload);
    }

    [Fact]
    public async Task Seal_Passphrase_NormalizesNfkcSoComposedAndDecomposedMatch()
    {
        const string composed = "café passphrase";
        const string decomposed = "café passphrase";
        var linked = await SealAsync("{\"name\":\"nfkc\"}", composed);

        var opened = await OpenAsync(linked.Payload, linked.Fragment, decomposed);

        Assert.Equal("{\"name\":\"nfkc\"}", opened.Plaintext);
    }

    [Fact]
    public async Task Seal_Fragment_HasThePPrefixOnlyWithAPassphrase()
    {
        var plain = await SealAsync("{\"name\":\"plain\"}");
        var guarded = await SealAsync("{\"name\":\"guarded\"}", Passphrase);

        Assert.False(plain.Fragment.StartsWith("p.", StringComparison.Ordinal));
        Assert.StartsWith("p.", guarded.Fragment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seal_Payload_NeverContainsThePlaintextBytes()
    {
        var linked = await SealAsync(Marker);

        var index = linked.Payload.AsSpan().IndexOf(Encoding.UTF8.GetBytes(Marker).AsSpan());

        Assert.Equal(-1, index);
    }

    private static async Task<SealedLink> SealAsync(string plaintext, string? passphrase = null)
    {
        var results = await RunAsync(new { op = "seal", plaintext, passphrase });
        var result = results.Single();
        return new SealedLink(Convert.FromBase64String(result.Payload!), result.Fragment!);
    }

    private static async Task<ScriptResult> OpenAsync(byte[] payload, string fragment, string? passphrase = null)
    {
        var results = await RunAsync(new { op = "open", payload = Convert.ToBase64String(payload), fragment, passphrase });
        return results.Single();
    }

    private static async Task<IReadOnlyList<ScriptResult>> RunAsync(object step)
    {
        var scenario = JsonSerializer.Serialize(new { steps = new[] { step } });
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(HarnessPath);
        startInfo.ArgumentList.Add(ModulePath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("node could not be started.");
        await process.StandardInput.WriteAsync(scenario.AsMemory(), Xunit.TestContext.Current.CancellationToken);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync(Xunit.TestContext.Current.CancellationToken);
        var error = await process.StandardError.ReadToEndAsync(Xunit.TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.True(process.ExitCode == 0, error);
        return JsonSerializer.Deserialize<List<ScriptResult>>(output, ResultOptions)!;
    }

    private static byte[] SealPassphraseInDotNet(byte[] linkKey, string passphrase, string plaintext)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        var iterations = BitConverter.GetBytes((uint)PassphraseIterations);
        Array.Reverse(iterations);
        var header = Concat([PassphraseVersion], salt, iterations, iv);
        var passphraseKey = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormKC)),
            salt,
            PassphraseIterations,
            HashAlgorithmName.SHA256,
            KeyBytes);
        var aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, Concat(linkKey, passphraseKey), KeyBytes, salt, Encoding.UTF8.GetBytes(PassphraseInfo));
        return Concat(header, Encrypt(aesKey, iv, header, Encoding.UTF8.GetBytes(plaintext)));
    }

    [SuppressMessage("Security", "CA5390", Justification = "The key is derived from random link material in the test, not a hard-coded secret.")]
    private static byte[] Encrypt(byte[] key, byte[] iv, byte[] associatedData, byte[] plaintext)
    {
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(iv, plaintext, ciphertext, tag, associatedData);
        return Concat(ciphertext, tag);
    }

    private static string PassphraseFragment(byte[] linkKey) => "p." + Base64Url.EncodeToString(linkKey);

    private static byte[] Tamper(byte[] payload, int index)
    {
        var copy = (byte[])payload.Clone();
        copy[index] ^= 0x01;
        return copy;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];

    private static byte[] Concat(byte[] first, byte[] second) => [.. first, .. second];

    private sealed record SealedLink(byte[] Payload, string Fragment);

    private sealed record ScriptResult(string? Payload, string? Fragment, string? Plaintext, string? Error);
}
