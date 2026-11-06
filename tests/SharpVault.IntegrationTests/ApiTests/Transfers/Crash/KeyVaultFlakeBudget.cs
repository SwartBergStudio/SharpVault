// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using SharpVault.Domain.Encryption;
using SharpVault.Infrastructure.Encryption;

namespace SharpVault.IntegrationTests.ApiTests.Transfers.Crash;

public sealed class KeyVaultFlakeBudget
{
    private const int TransientStatus = 503;
    private const string TransientMessage = "Simulated transient Key Vault failure.";

    private int _remaining;
    private int _consumed;

    public int Consumed => Volatile.Read(ref _consumed);

    public void Arm(int failures) => Volatile.Write(ref _remaining, failures);

    public void ThrowIfFailureDue()
    {
        if (!TryTakeFailure())
        {
            return;
        }

        throw new RequestFailedException(TransientStatus, TransientMessage);
    }

    private bool TryTakeFailure()
    {
        while (true)
        {
            var remaining = Volatile.Read(ref _remaining);
            if (remaining <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _remaining, remaining - 1, remaining) == remaining)
            {
                Interlocked.Increment(ref _consumed);
                return true;
            }
        }
    }
}

public sealed class FlakyKeyWrapProvider(IKeyWrapProvider inner, KeyVaultFlakeBudget budget) : IKeyWrapProvider
{
    public async Task<WrappedDataKey> WrapAsync(byte[] dataKey, CancellationToken cancellationToken)
    {
        budget.ThrowIfFailureDue();
        return await inner.WrapAsync(dataKey, cancellationToken);
    }

    public async Task<byte[]> UnwrapAsync(WrappedDataKey wrappedKey, CancellationToken cancellationToken)
    {
        budget.ThrowIfFailureDue();
        return await inner.UnwrapAsync(wrappedKey, cancellationToken);
    }
}
