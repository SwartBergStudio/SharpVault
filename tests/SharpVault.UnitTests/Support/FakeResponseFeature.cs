// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace SharpVault.UnitTests.Support;

internal sealed class FakeResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

    public int StatusCode { get; set; } = StatusCodes.Status200OK;

    public string? ReasonPhrase { get; set; }

    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    public Stream Body { get; set; } = Stream.Null;

    public bool HasStarted { get; private set; }

    public Stream Stream => Body;

    public System.IO.Pipelines.PipeWriter Writer => System.IO.Pipelines.PipeWriter.Create(Body);

    public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

    public void OnCompleted(Func<object, Task> callback, object state)
    {
    }

    public async Task FireOnStartingAsync()
    {
        HasStarted = true;
        for (var index = _onStarting.Count - 1; index >= 0; index--)
        {
            await _onStarting[index].Callback(_onStarting[index].State);
        }
    }

    public void DisableBuffering()
    {
    }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CompleteAsync() => Task.CompletedTask;
}
