// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Azure.Core;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;

namespace SharpVault.Infrastructure.Directory;

public sealed class GraphUserDirectory : IUserDirectory
{
    public const string GraphBaseAddress = "https://graph.microsoft.com/v1.0/";

    public const string GraphScope = "https://graph.microsoft.com/.default";

    private const string ConsistencyLevelHeader = "ConsistencyLevel";
    private const string ConsistencyLevelEventual = "eventual";
    private const string GuestMarker = "#EXT#";
    private const string SelectFields = "id,displayName,mail,userPrincipalName";

    private readonly HttpClient _http;
    private readonly GraphCredential _credential;
    private readonly IGuestAccessPolicy _guests;

    public GraphUserDirectory(HttpClient http, GraphCredential credential, IGuestAccessPolicy guests)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(guests);

        http.BaseAddress = new Uri(GraphBaseAddress);
        _http = http;
        _credential = credential;
        _guests = guests;
    }

    public async Task<IReadOnlyList<DirectoryUser>> SearchAsync(
        string term,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(term);

        var safeTerm = Sanitise(term);
        var expression = $"\"displayName:{safeTerm}\" OR \"mail:{safeTerm}\"";
        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"users?$search={Uri.EscapeDataString(expression)}&$select={SelectFields}&$top={limit}");

        using var request = await CreateRequestAsync(path, cancellationToken);
        request.Headers.TryAddWithoutValidation(ConsistencyLevelHeader, ConsistencyLevelEventual);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<GraphUserResponse>(cancellationToken);
        var allowGuests = await _guests.AreGuestsAllowedAsync(cancellationToken);

        return (body?.Value ?? [])
            .Where(item => allowGuests || !IsGuest(item))
            .Select(ToDirectoryUser)
            .ToList();
    }

    public async Task<DirectoryUser?> FindMemberAsync(Guid userId, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"users/{userId:D}?$select={SelectFields}");

        using var request = await CreateRequestAsync(path, cancellationToken);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync<GraphUserItem>(cancellationToken);
        if (item is null || (IsGuest(item) && !await _guests.AreGuestsAllowedAsync(cancellationToken)))
        {
            return null;
        }

        return ToDirectoryUser(item);
    }

    private static string Sanitise(string term) =>
        term.Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("\\", string.Empty, StringComparison.Ordinal);

    private static bool IsGuest(GraphUserItem item) =>
        item.UserPrincipalName?.Contains(GuestMarker, StringComparison.OrdinalIgnoreCase) == true;

    private static DirectoryUser ToDirectoryUser(GraphUserItem item) =>
        new(item.Id, item.DisplayName ?? item.UserPrincipalName ?? string.Empty, item.Mail);

    private async Task<HttpRequestMessage> CreateRequestAsync(string path, CancellationToken cancellationToken)
    {
        var token = await _credential.Credential.GetTokenAsync(
            new TokenRequestContext([GraphScope]),
            cancellationToken);
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return request;
    }
}
