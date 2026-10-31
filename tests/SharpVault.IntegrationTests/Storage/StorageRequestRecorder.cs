// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.AspNetCore.WebUtilities;

namespace SharpVault.IntegrationTests.Storage;

public sealed class StorageRequestRecorder : HttpPipelinePolicy
{
    private const string FilterParameter = "$filter";

    private readonly ConcurrentQueue<RecordedStorageRequest> _requests = new();

    public IReadOnlyList<RecordedStorageRequest> Requests => [.. _requests];

    public int Count => _requests.Count;

    public void Clear() => _requests.Clear();

    public IReadOnlyList<RecordedStorageRequest> QueriesWithoutPartitionKey() =>
        [.. _requests.Where(request => request.IsEntityQuery && !request.HasPartitionKeyCondition)];

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        Record(message);
        ProcessNext(message, pipeline);
    }

    public override ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        Record(message);
        return ProcessNextAsync(message, pipeline);
    }

    private void Record(HttpMessage message)
    {
        var uri = message.Request.Uri.ToUri();
        var query = QueryHelpers.ParseQuery(uri.Query);
        string? filter = query.TryGetValue(FilterParameter, out var values) ? values.ToString() : null;
        _requests.Enqueue(new RecordedStorageRequest(message.Request.Method.Method, uri.AbsolutePath, filter));
    }
}
