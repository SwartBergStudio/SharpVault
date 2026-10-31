// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Globalization;

namespace SharpVault.IntegrationTests.Tracing;

public sealed record RecordedActivity(string SourceName, string DisplayName, IReadOnlyDictionary<string, string> Tags)
{
    public static RecordedActivity From(Activity activity)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in activity.TagObjects)
        {
            tags[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return new RecordedActivity(activity.Source.Name, activity.DisplayName, tags);
    }

    public bool Targets(string uniquePath) =>
        (Tags.TryGetValue(TraceTagNames.UrlPath, out var path) && string.Equals(path, uniquePath, StringComparison.Ordinal))
        || (Tags.TryGetValue(TraceTagNames.UrlFull, out var full) && full.Contains(uniquePath, StringComparison.Ordinal));

    public bool ContainsText(string value) =>
        DisplayName.Contains(value, StringComparison.Ordinal)
        || Tags.Any(tag => tag.Key.Contains(value, StringComparison.Ordinal) || tag.Value.Contains(value, StringComparison.Ordinal));
}
