// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

[Trait("Category", "Security")]
public sealed class DataKeySessionSharedUseTests
{
    private const int Workers = 16;

    private const int OperationsPerWorker = 300;

    [Fact]
    public async Task Session_OneInstanceUsedByManyThreads_EveryOperationRoundTrips()
    {
        var harness = new EncryptionHarness();
        using var session = await harness.CreateSessionAsync();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        await Parallel.ForAsync(0, Workers, new ParallelOptions { MaxDegreeOfParallelism = Workers }, async (worker, _) =>
        {
            await Task.Yield();
            for (var operation = 0; operation < OperationsPerWorker; operation++)
            {
                var context = EncryptionContexts.Random();
                var text = $"worker-{worker}-operation-{operation}";
                var envelope = session.Encrypt(context, text);
                if (session.Decrypt(context, envelope) != text)
                {
                    failures.Add(text);
                }
            }
        });

        Assert.Empty(failures);
    }
}
