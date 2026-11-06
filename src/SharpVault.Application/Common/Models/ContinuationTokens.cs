// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpVault.Application.Common.Models;

public static partial class ContinuationTokens
{
    public const int MaxLength = 1024;

    private const char StoragePrefix = 't';
    private const string SecretIdPattern = @"^[0-9a-f]{32}\z";
    private const string BinKeyPattern = @"^[fs]_[0-9a-f]{32}\z";
    private const string InboxKeyPattern = @"^[0-9a-f]{32}_[fs]_[0-9a-f]{32}\z";
    private const string SecretIdFormat = "N";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string ForSecret(Guid secretId) => secretId.ToString(SecretIdFormat);

    public static string ForInbox(string rowKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(rowKey);
        return rowKey;
    }

    public static string ForStorage(string storageToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(storageToken);
        return StoragePrefix + Base64Url.EncodeToString(StrictUtf8.GetBytes(storageToken));
    }

    public static bool IsSecretToken(string? token) =>
        token is not null && (TryGetSecretId(token, out _) || TryGetStorageToken(token, out _));

    public static bool IsRecycleBinToken(string? token) =>
        token is not null && (TryGetBinKey(token, out _) || TryGetStorageToken(token, out _));

    public static bool IsInboxToken(string? token) =>
        token is not null && (TryGetInboxKey(token, out _) || TryGetStorageToken(token, out _));

    public static bool IsStorageToken(string? token) =>
        token is not null && TryGetStorageToken(token, out _);

    public static bool TryGetSecretId(string token, out Guid secretId)
    {
        secretId = Guid.Empty;
        return token.Length <= MaxLength
            && SecretIdRegex().IsMatch(token)
            && Guid.TryParseExact(token, SecretIdFormat, out secretId);
    }

    public static bool TryGetBinKey(string token, out string key)
    {
        key = token;
        return token.Length <= MaxLength && BinKeyRegex().IsMatch(token);
    }

    public static bool TryGetInboxKey(string token, out string key)
    {
        key = token;
        return token.Length <= MaxLength && InboxKeyRegex().IsMatch(token);
    }

    public static bool TryGetStorageToken(string token, out string storageToken)
    {
        storageToken = string.Empty;
        if (token.Length < 2 || token.Length > MaxLength || token[0] != StoragePrefix)
        {
            return false;
        }

        var payload = token.AsSpan(1);
        if (!Base64Url.IsValid(payload))
        {
            return false;
        }

        try
        {
            storageToken = StrictUtf8.GetString(Base64Url.DecodeFromChars(payload));
            return storageToken.Length > 0;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    [GeneratedRegex(SecretIdPattern, RegexOptions.CultureInvariant)]
    private static partial Regex SecretIdRegex();

    [GeneratedRegex(InboxKeyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex InboxKeyRegex();

    [GeneratedRegex(BinKeyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex BinKeyRegex();
}
