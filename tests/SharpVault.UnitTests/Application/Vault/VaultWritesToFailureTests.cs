// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging.Abstractions;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class VaultWritesToFailureTests
{
    [Fact]
    public void ToFailure_Interruption_RethrowsOriginalException()
    {
        var failure = new TimeoutException("storage down");
        var write = new VaultWriteResult(StoreWriteResult.Conflict, 5, [], ExceptionDispatchInfo.Capture(failure));

        var thrown = Assert.Throws<TimeoutException>(() => VaultWrites.ToFailure<Done>(write, NullLogger.Instance, Guid.NewGuid()));

        Assert.Same(failure, thrown);
    }

    [Theory]
    [InlineData(StoreWriteResult.Written, null)]
    [InlineData(StoreWriteResult.NotFound, ResultFailureKind.NotFound)]
    [InlineData(StoreWriteResult.Conflict, ResultFailureKind.Conflict)]
    [InlineData(StoreWriteResult.AlreadyExists, ResultFailureKind.Conflict)]
    public void ToFailure_NoInterruption_MapsAsBefore(StoreWriteResult result, ResultFailureKind? expected)
    {
        var failure = VaultWrites.ToFailure<Done>(new VaultWriteResult(result, 0, []), NullLogger.Instance, Guid.NewGuid());

        Assert.Equal(expected, failure?.FailureKind);
    }
}
