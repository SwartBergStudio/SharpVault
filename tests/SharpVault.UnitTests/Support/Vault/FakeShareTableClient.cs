// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Infrastructure.Sharing;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class FakeShareTableClient : TableClient
{
    private const string TokenPrefix = "next-";

    public Func<int, Page<ShareGrantTableEntity>> GrantPageAt { get; set; } = _ => GrantPageOf([], null);

    public Func<int, Page<ShareInboxTableEntity>> InboxPageAt { get; set; } = _ => InboxPageOf([], null);

    public Func<ShareInboxTableEntity?> CurrentInboxRow { get; set; } = () => null;

    public Func<int, Exception?> DeleteOutcome { get; set; } = _ => null;

    public int PagesRead { get; private set; }

    public int Gets { get; private set; }

    public int Deletes { get; private set; }

    public List<string?> Filters { get; } = [];

    public List<int?> PageSizes { get; } = [];

    public List<string?> ResumeTokens { get; } = [];

    public static Page<ShareGrantTableEntity> GrantPageOf(IReadOnlyList<ShareGrantTableEntity> values, string? token) =>
        Page<ShareGrantTableEntity>.FromValues(values, token, new FakeAzureResponse(200, null));

    public static Page<ShareGrantTableEntity> EmptyGrantPageWithMore(int index) => GrantPageOf([], TokenPrefix + index);

    public static Page<ShareInboxTableEntity> InboxPageOf(IReadOnlyList<ShareInboxTableEntity> values, string? token) =>
        Page<ShareInboxTableEntity>.FromValues(values, token, new FakeAzureResponse(200, null));

    public static Page<ShareInboxTableEntity> EmptyInboxPageWithMore(int index) => InboxPageOf([], TokenPrefix + index);

    public static string TokenAfter(int index) => TokenPrefix + index;

    public override AsyncPageable<T> QueryAsync<T>(
        string? filter = null,
        int? maxPerPage = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        Filters.Add(filter);
        PageSizes.Add(maxPerPage);

        if (typeof(T) == typeof(ShareGrantTableEntity))
        {
            return (AsyncPageable<T>)(object)new IndexedPageable<ShareGrantTableEntity>(this, GrantPageAt);
        }

        if (typeof(T) == typeof(ShareInboxTableEntity))
        {
            return (AsyncPageable<T>)(object)new IndexedPageable<ShareInboxTableEntity>(this, InboxPageAt);
        }

        throw new NotSupportedException();
    }

    public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(
        string partitionKey,
        string rowKey,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        Gets++;
        var row = CurrentInboxRow();
        return Task.FromResult<NullableResponse<T>>(new FakeNullableResponse<T>((T?)(object?)row));
    }

    public override Task<Response> DeleteEntityAsync(
        string partitionKey,
        string rowKey,
        ETag ifMatch = default,
        CancellationToken cancellationToken = default)
    {
        Deletes++;
        var outcome = DeleteOutcome(Deletes);
        return outcome is null ? Task.FromResult<Response>(new FakeAzureResponse(204, null)) : Task.FromException<Response>(outcome);
    }

    private sealed class IndexedPageable<T>(FakeShareTableClient owner, Func<int, Page<T>> pageAt) : AsyncPageable<T>
        where T : notnull
    {
        public override async IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
        {
            owner.ResumeTokens.Add(continuationToken);
            var index = continuationToken is null ? 0 : int.Parse(continuationToken[TokenPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture) + 1;
            while (true)
            {
                owner.PagesRead++;
                var page = pageAt(index);
                yield return page;
                if (page.ContinuationToken is null)
                {
                    yield break;
                }

                index++;
                await Task.Yield();
            }
        }
    }
}
