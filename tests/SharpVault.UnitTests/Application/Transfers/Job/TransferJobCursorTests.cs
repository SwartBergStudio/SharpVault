// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using NSubstitute;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Transfers;

namespace SharpVault.UnitTests.Application.Transfers.Job;

public sealed class TransferJobCursorTests
{
    private const string FirstETag = "etag-1";
    private const string SavedETag = "etag-2";

    private readonly ITransferJobStore _jobs = Substitute.For<ITransferJobStore>();
    private readonly TransferJob _job = new() { Id = Guid.NewGuid(), State = TransferState.Approved };
    private readonly TransferJobCursor _cursor;

    public TransferJobCursorTests()
    {
        _cursor = new TransferJobCursor(_jobs, new Versioned<TransferJob>(_job, FirstETag));
    }

    [Fact]
    public void Job_ReturnsTheInitialValue() =>
        Assert.Same(_job, _cursor.Job);

    [Fact]
    public async Task SaveAsync_Written_AdoptsTheStoredVersion()
    {
        var saved = new TransferJob { Id = _job.Id, State = TransferState.Staging };
        _jobs.ReplaceAsync(_job, FirstETag, TestContext.Current.CancellationToken)
            .Returns(Task.FromResult<Versioned<TransferJob>?>(new Versioned<TransferJob>(saved, SavedETag)));

        await _cursor.SaveAsync(TestContext.Current.CancellationToken);

        Assert.Same(saved, _cursor.Job);
    }

    [Fact]
    public async Task SaveAsync_AfterAWrite_SendsTheStoredETagNextTime()
    {
        var saved = new TransferJob { Id = _job.Id, State = TransferState.Staging };
        _jobs.ReplaceAsync(_job, FirstETag, TestContext.Current.CancellationToken)
            .Returns(Task.FromResult<Versioned<TransferJob>?>(new Versioned<TransferJob>(saved, SavedETag)));
        _jobs.ReplaceAsync(saved, SavedETag, TestContext.Current.CancellationToken)
            .Returns(Task.FromResult<Versioned<TransferJob>?>(new Versioned<TransferJob>(saved, "etag-3")));

        await _cursor.SaveAsync(TestContext.Current.CancellationToken);
        await _cursor.SaveAsync(TestContext.Current.CancellationToken);

        _ = _jobs.Received(1).ReplaceAsync(saved, SavedETag, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SaveAsync_ChangedETag_ThrowsTransferJobChanged()
    {
        _jobs.ReplaceAsync(_job, FirstETag, TestContext.Current.CancellationToken)
            .Returns(Task.FromResult<Versioned<TransferJob>?>(null));

        await Assert.ThrowsAsync<TransferJobChangedException>(() => _cursor.SaveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SaveAsync_AfterAChangedETag_StaysOnTheStaleVersionAndFailsAgain()
    {
        _jobs.ReplaceAsync(_job, FirstETag, TestContext.Current.CancellationToken)
            .Returns(Task.FromResult<Versioned<TransferJob>?>(null));
        await Assert.ThrowsAsync<TransferJobChangedException>(() => _cursor.SaveAsync(TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<TransferJobChangedException>(() => _cursor.SaveAsync(TestContext.Current.CancellationToken));

        Assert.Same(_job, _cursor.Job);
        _ = _jobs.Received(2).ReplaceAsync(_job, FirstETag, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CloseAsync_Written_CompletesWithTheCurrentETag()
    {
        _jobs.CloseAsync(_job, FirstETag, TestContext.Current.CancellationToken).Returns(Task.FromResult(StoreWriteResult.Written));

        await _cursor.CloseAsync(TestContext.Current.CancellationToken);

        _ = _jobs.Received(1).CloseAsync(_job, FirstETag, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(StoreWriteResult.AlreadyExists)]
    [InlineData(StoreWriteResult.Conflict)]
    [InlineData(StoreWriteResult.NotFound)]
    public async Task CloseAsync_NotWritten_ThrowsTransferJobChanged(StoreWriteResult result)
    {
        _jobs.CloseAsync(_job, FirstETag, TestContext.Current.CancellationToken).Returns(Task.FromResult(result));

        await Assert.ThrowsAsync<TransferJobChangedException>(() => _cursor.CloseAsync(TestContext.Current.CancellationToken));
    }
}
