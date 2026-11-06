// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Support;

public sealed partial class TestDirectoryHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> _requests = new();

    public int Calls => _requests.Count;

    public IReadOnlyList<string> Requests => [.. _requests];

    public void Clear() => _requests.Clear();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var uri = request.RequestUri ?? throw new InvalidOperationException("The directory request had no address.");
        _requests.Enqueue(uri.PathAndQuery);

        var path = uri.AbsolutePath.TrimEnd('/');
        var point = PointRead().Match(path);
        if (point.Success)
        {
            return Task.FromResult(PointResponse(Guid.Parse(point.Groups["id"].Value)));
        }

        return Task.FromResult(path.EndsWith("/users", StringComparison.Ordinal) ? SearchResponse(uri) : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage PointResponse(Guid id)
    {
        var user = Directory().FirstOrDefault(candidate => candidate.ObjectId == id);
        return user is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(Item(user));
    }

    private static HttpResponseMessage SearchResponse(Uri uri)
    {
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var term = SearchTerm().Match(query["$search"] ?? string.Empty).Groups["term"].Value;
        var top = int.TryParse(query["$top"], out var parsed) ? parsed : int.MaxValue;
        var matches = Directory()
            .Where(user => user.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (user.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(top)
            .Select(Item)
            .ToList();
        return Json(new { value = matches });
    }

    private static IEnumerable<TestUser> Directory() =>
        TestUsers.All.Where(user => user.TenantId == TestTenant.Id && user != TestUsers.AuthenticatedWithoutRole);

    private static object Item(TestUser user) => new
    {
        id = user.ObjectId,
        displayName = user.DisplayName,
        mail = user.Email,
        userPrincipalName = user.IsGuest ? $"{user.Email}#EXT#@tenant.test" : user.Email,
    };

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    [GeneratedRegex(@"/users/(?<id>[0-9a-fA-F-]{36})\z", RegexOptions.CultureInvariant)]
    private static partial Regex PointRead();

    [GeneratedRegex("\"displayName:(?<term>[^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex SearchTerm();
}
