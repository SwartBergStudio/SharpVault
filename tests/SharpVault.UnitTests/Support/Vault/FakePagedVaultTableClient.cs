// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class FakePagedVaultTableClient : TableClient
{
    private const int MaxFakePages = 1000;

    public Func<int, Page<SecretTableEntity>> PageAt { get; set; } = _ => Page<SecretTableEntity>.FromValues([], null, new FakeAzureResponse(200, null));

    public Func<int, Page<TableEntity>> TablePageAt { get; set; } = _ => Page<TableEntity>.FromValues([], null, new FakeAzureResponse(200, null));

    public int PagesRead { get; private set; }

    public List<string?> Filters { get; } = [];

    public List<int?> PageSizes { get; } = [];

    public override AsyncPageable<T> QueryAsync<T>(
        string? filter = null,
        int? maxPerPage = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        Filters.Add(filter);
        PageSizes.Add(maxPerPage);
        if (typeof(T) == typeof(TableEntity))
        {
            return (AsyncPageable<T>)(object)AsyncPageable<TableEntity>.FromPages(Pages(TablePageAt));
        }

        return (AsyncPageable<T>)(object)AsyncPageable<SecretTableEntity>.FromPages(Pages(PageAt));
    }

    public static Page<SecretTableEntity> EmptyPageWithMore(int index) =>
        Page<SecretTableEntity>.FromValues([], "next-" + index, new FakeAzureResponse(200, null));

    public static Page<SecretTableEntity> PageOf(IReadOnlyList<SecretTableEntity> values, string? token) =>
        Page<SecretTableEntity>.FromValues(values, token, new FakeAzureResponse(200, null));

    public static Page<TableEntity> EmptyTablePageWithMore(int index) =>
        Page<TableEntity>.FromValues([], "next-" + index, new FakeAzureResponse(200, null));

    public static Page<TableEntity> TablePageOf(IReadOnlyList<TableEntity> values, string? token) =>
        Page<TableEntity>.FromValues(values, token, new FakeAzureResponse(200, null));

    private IEnumerable<Page<TEntity>> Pages<TEntity>(Func<int, Page<TEntity>> pageAt)
        where TEntity : notnull
    {
        for (var index = 0; index < MaxFakePages; index++)
        {
            PagesRead++;
            var page = pageAt(index);
            yield return page;
            if (page.ContinuationToken is null)
            {
                yield break;
            }
        }
    }
}
