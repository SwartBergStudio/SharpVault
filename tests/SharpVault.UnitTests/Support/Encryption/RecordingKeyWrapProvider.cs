// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Encryption;
using SharpVault.Infrastructure.Encryption;

namespace SharpVault.UnitTests.Support.Encryption;

internal sealed class RecordingKeyWrapProvider : IKeyWrapProvider
{
    public const string KeyId = "recording:1";
    public const string Algorithm = "recording";
    private const int HandleLength = 16;
    private const byte OverrideFill = 0xAB;

    private readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<byte[]> _wrapInputs = new();
    private readonly ConcurrentQueue<byte[]> _unwrapOutputs = new();
    private int _wrapCount;
    private int _unwrapCount;

    public int WrapCount => Volatile.Read(ref _wrapCount);

    public int UnwrapCount => Volatile.Read(ref _unwrapCount);

    public IReadOnlyList<byte[]> WrapInputs => [.. _wrapInputs];

    public IReadOnlyList<byte[]> UnwrapOutputs => [.. _unwrapOutputs];

    public Func<Exception>? WrapFailure { get; set; }

    public Func<Exception>? UnwrapFailure { get; set; }

    public int? UnwrapLengthOverride { get; set; }

    public Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _wrapCount);
        _wrapInputs.Enqueue(dataKey);
        cancellationToken.ThrowIfCancellationRequested();

        if (WrapFailure is { } failure)
        {
            throw failure();
        }

        var handle = RandomNumberGenerator.GetBytes(HandleLength);
        _keys[Convert.ToHexString(handle)] = [.. dataKey];
        return Task.FromResult(new WrappedDataKey(KeyId, Algorithm, handle));
    }

    public Task<byte[]> UnwrapAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _unwrapCount);
        cancellationToken.ThrowIfCancellationRequested();

        if (UnwrapFailure is { } failure)
        {
            throw failure();
        }

        if (!_keys.TryGetValue(Convert.ToHexString(wrappedKey.Value), out var stored))
        {
            throw new SecretDecryptionException(DecryptionFailureReason.KeyMismatch);
        }

        var output = UnwrapLengthOverride is { } length
            ? Enumerable.Repeat(OverrideFill, length).ToArray()
            : [.. stored];
        _unwrapOutputs.Enqueue(output);
        return Task.FromResult(output);
    }
}
