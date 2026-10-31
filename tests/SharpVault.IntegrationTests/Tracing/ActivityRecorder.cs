// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Diagnostics;

namespace SharpVault.IntegrationTests.Tracing;

public sealed class ActivityRecorder : IDisposable
{
    public const string AspNetCoreSource = "Microsoft.AspNetCore";
    public const string HttpClientSource = "System.Net.Http";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly string _uniquePath;
    private readonly ConcurrentQueue<RecordedActivity> _recorded = new();
    private readonly TaskCompletionSource _targeted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ActivityListener _listener;

    public ActivityRecorder(string uniquePath, params string[] sourceNames)
    {
        _uniquePath = uniquePath;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => sourceNames.Contains(source.Name, StringComparer.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<RecordedActivity> All => _recorded.ToArray();

    public IReadOnlyList<RecordedActivity> Targeted => [.. _recorded.Where(activity => activity.Targets(_uniquePath))];

    public bool AnyContains(string value) => _recorded.Any(activity => activity.ContainsText(value));

    public Task WaitForTargetedActivityAsync(CancellationToken cancellationToken) =>
        _targeted.Task.WaitAsync(WaitTimeout, cancellationToken);

    public void Dispose() => _listener.Dispose();

    private void OnStopped(Activity activity)
    {
        var snapshot = RecordedActivity.From(activity);
        _recorded.Enqueue(snapshot);
        if (snapshot.Targets(_uniquePath))
        {
            _targeted.TrySetResult();
        }
    }
}
