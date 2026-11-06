// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(PerformanceTestGroup))]
[Trait("Category", "Performance")]
public sealed partial class VaultPerformanceTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private const int VaultSize = 5000;
    private const int PageSize = 50;
    private const int WarmIterations = 5;
    private const int MeasuredIterations = 20;
    private const int HeavyWarmIterations = 1;
    private const int HeavyMeasuredIterations = 3;
    private const double P95Factor = 2;
    private const int FolderLimit = 1000;
    private const int BulkFolders = 10;
    private const int BulkSecrets = 500;
    private const int BatchActionLimit = 99;
    private const int TableQueryPageSize = 1000;
    private const int ParallelReveals = 20;
    private const int PurgeUsers = 10;
    private const int PurgeSecretsPerUser = 99;
    private const int PurgeLeaseWrites = 3;
    private const string UserRole = "SharpVault.User";
    private const string NonMatchingSearch = "no-such-name-in-this-vault";

    private static readonly TimeSpan ListBudget = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan GetBudget = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RevealBudget = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan WriteBudget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan BulkBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PurgeBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ParallelBudget = TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    [GeneratedRegex(@"^/[^/]+/Vaults(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex VaultsPath();

    [GeneratedRegex(@"^/[^/]+/Maintenance(\(|$)", RegexOptions.CultureInvariant)]
    private static partial Regex MaintenancePath();

    [GeneratedRegex(@"^/[^/]+/\$batch$", RegexOptions.CultureInvariant)]
    private static partial Regex BatchPath();

    private sealed record Counts(int VaultReads, int VaultWrites, int AuditWrites, int Unwraps, int Wraps);

    private sealed record Measurement(double MedianMs, double P95Ms, double MaxMs);

    private Counts Measure()
    {
        var counts = new Counts(
            StorageRequests.VaultReads().Count,
            StorageRequests.Requests.Count(request => request.Method != "GET" && (VaultsPath().IsMatch(request.Path) || BatchPath().IsMatch(request.Path))),
            StorageRequests.AuditWrites().Count,
            Factory.KeyWraps.Unwraps,
            Factory.KeyWraps.Wraps);
        TestContext.Current.TestOutputHelper?.WriteLine($"Counts: {counts}");
        return counts;
    }

    private async Task<SeededVault> SeededVaultAsync(int secrets, int folders = 0)
    {
        await WarmUpAsync(Caller);
        return await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, secrets, folders, Token);
    }

    private static Measurement Summarise(List<double> samplesMs)
    {
        samplesMs.Sort();
        var middle = samplesMs.Count / 2;
        var median = samplesMs.Count % 2 == 1 ? samplesMs[middle] : (samplesMs[middle - 1] + samplesMs[middle]) / 2;
        var p95 = samplesMs[(int)Math.Ceiling(0.95 * samplesMs.Count) - 1];
        return new Measurement(median, p95, samplesMs[^1]);
    }

    private static Task<Measurement> MeasureAsync(string name, TimeSpan budget, Func<Task> operation) =>
        MeasureWithSetupAsync(name, budget, () => Task.FromResult(operation), WarmIterations, MeasuredIterations);

    private static async Task<Measurement> MeasureWithSetupAsync(string name, TimeSpan budget, Func<Task<Func<Task>>> setup, int warm, int measured)
    {
        for (var index = 0; index < warm; index++)
        {
            var warmOperation = await setup();
            await warmOperation();
        }

        var samples = new List<double>();
        for (var index = 0; index < measured; index++)
        {
            var operation = await setup();
            var started = Stopwatch.GetTimestamp();
            await operation();
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        var summary = Summarise(samples);
        var limit = budget.TotalMilliseconds * P95Factor;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name}: median {summary.MedianMs:F1} ms, p95 {summary.P95Ms:F1} ms, max {summary.MaxMs:F1} ms, budget {budget.TotalMilliseconds:F0} ms, samples {samples.Count}"));
        Assert.True(summary.MedianMs < budget.TotalMilliseconds, string.Create(CultureInfo.InvariantCulture, $"{name} median {summary.MedianMs:F1} ms, budget {budget.TotalMilliseconds:F0} ms"));
        Assert.True(summary.P95Ms < limit, string.Create(CultureInfo.InvariantCulture, $"{name} p95 {summary.P95Ms:F1} ms, limit {limit:F0} ms"));
        return summary;
    }

    private static void AssertOk<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.NotNull(result.Body);
    }

    private void AssertNoTableScan() => Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());

    [Fact]
    public async Task ListSecrets_PageOf50FromVaultOf5000_Under500Ms()
    {
        await SeededVaultAsync(VaultSize);
        ResetCounters();
        var single = await Api.ListSecretsAsync(pageSize: PageSize);
        AssertOk(single);
        Assert.Equal(PageSize, single.Body!.Items.Count);
        Assert.NotNull(single.Body.ContinuationToken);
        Assert.Equal(new Counts(2, 0, 0, 1, 0), Measure());
        AssertNoTableScan();

        await MeasureAsync(nameof(ListSecrets_PageOf50FromVaultOf5000_Under500Ms), ListBudget, async () => AssertOk(await Api.ListSecretsAsync(pageSize: PageSize)));
    }

    [Fact]
    public async Task ListSecrets_FolderFilterInVaultOf5000_Under500Ms()
    {
        await WarmUpAsync(Caller);
        var folder = await NewFolderAsync("Filtered");
        await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, VaultSize - 500, 0, Token);
        await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, 500, 0, new SeedPlacement(null, folder.Id), Token);
        ResetCounters();
        var single = await Api.ListSecretsAsync(folderId: folder.Id, pageSize: PageSize);
        AssertOk(single);
        Assert.Equal(PageSize, single.Body!.Items.Count);
        AssertNoTableScan();
        var counts = Measure();
        Assert.Equal(new Counts(counts.VaultReads, 0, 0, 1, 0), counts);
        Assert.InRange(counts.VaultReads, 2, 3);

        await MeasureAsync(nameof(ListSecrets_FolderFilterInVaultOf5000_Under500Ms), ListBudget, async () => AssertOk(await Api.ListSecretsAsync(folderId: folder.Id, pageSize: PageSize)));
    }

    [Fact]
    public async Task Search_FullScanOf5000_Under1500Ms()
    {
        await SeededVaultAsync(VaultSize);
        ResetCounters();
        var single = await Api.ListSecretsAsync(search: NonMatchingSearch, pageSize: PageSize);
        AssertOk(single);
        Assert.Empty(single.Body!.Items);
        Assert.Null(single.Body.ContinuationToken);
        var counts = Measure();
        Assert.Equal(new Counts(counts.VaultReads, 0, 0, 1, 0), counts);
        Assert.Equal(1 + (VaultSize / TableQueryPageSize), counts.VaultReads);
        AssertNoTableScan();

        await MeasureAsync(nameof(Search_FullScanOf5000_Under1500Ms), SearchBudget, async () => AssertOk(await Api.ListSecretsAsync(search: NonMatchingSearch, pageSize: PageSize)));
    }

    [Fact]
    public async Task GetSecret_InVaultOf5000_Under200Ms()
    {
        var vault = await SeededVaultAsync(VaultSize);
        var target = vault.Secrets[VaultSize / 2].Id;
        ResetCounters();
        AssertOk(await Api.GetSecretAsync(target));
        Assert.Equal(new Counts(2, 0, 0, 1, 0), Measure());
        AssertNoTableScan();

        await MeasureAsync(nameof(GetSecret_InVaultOf5000_Under200Ms), GetBudget, async () => AssertOk(await Api.GetSecretAsync(target)));
    }

    [Fact]
    public async Task RevealValue_Under200Ms()
    {
        await WarmUpAsync(Caller);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(Api, markers);
        var request = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };
        ResetCounters();
        var single = await Api.RevealSecretAsync(secret.Id, request);
        AssertOk(single);
        Assert.Equal(markers.Value, single.Body!.Value);
        Assert.Equal(new Counts(1, 0, 2, 1, 0), Measure());
        AssertNoTableScan();

        await MeasureAsync(nameof(RevealValue_Under200Ms), RevealBudget, async () => AssertOk(await Api.RevealSecretAsync(secret.Id, request)));
    }

    [Fact]
    public async Task CreateSecret_Under300Ms()
    {
        await WarmUpAsync(Caller);
        await NewSecretAsync();
        ResetCounters();
        var single = await Api.CreateSecretAsync(NewRequest());
        Assert.Equal(HttpStatusCode.Created, single.Response.StatusCode);
        Assert.Equal(new Counts(1, 1, 2, 1, 1), Measure());
        AssertNoTableScan();

        await MeasureAsync(nameof(CreateSecret_Under300Ms), WriteBudget, async () =>
        {
            var created = await Api.CreateSecretAsync(NewRequest());
            Assert.Equal(HttpStatusCode.Created, created.Response.StatusCode);
        });
    }

    [Fact]
    public async Task UpdateSecretValue_Under300Ms()
    {
        await WarmUpAsync(Caller);
        var current = await NewSecretAsync();
        var counter = 0;

        async Task UpdateAsync()
        {
            var body = UpdateFrom(current);
            body.ReplaceValue = true;
            body.Value = string.Create(CultureInfo.InvariantCulture, $"value-{++counter}");
            var result = await Api.UpdateSecretAsync(current.Id, current.ConcurrencyToken, body);
            Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
            current = result.Body!;
        }

        ResetCounters();
        await UpdateAsync();
        var counts = Measure();
        Assert.Equal(new Counts(counts.VaultReads, 1, 2, 2, 0), counts);
        Assert.InRange(counts.VaultReads, 2, 3);
        AssertNoTableScan();

        await MeasureAsync(nameof(UpdateSecretValue_Under300Ms), WriteBudget, UpdateAsync);
    }

    [Fact]
    public async Task ListFolders_1000Folders_Under500Ms()
    {
        await SeededVaultAsync(0, FolderLimit);
        ResetCounters();
        var single = await Api.ListFoldersAsync();
        AssertOk(single);
        Assert.Equal(FolderLimit, single.Body!.Items.Count);
        Assert.Equal(new Counts(2, 0, 0, 1, 0), Measure());
        AssertNoTableScan();

        await MeasureAsync(nameof(ListFolders_1000Folders_Under500Ms), ListBudget, async () => AssertOk(await Api.ListFoldersAsync()));
    }

    [Fact]
    public async Task DeleteFolderDeleteContents_10Folders500Secrets_Under5Seconds()
    {
        await WarmUpAsync(Caller);

        async Task<Func<Task>> SetupAsync()
        {
            var root = await NewFolderAsync("Bulk");
            await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, BulkSecrets, BulkFolders - 1, new SeedPlacement(root.Id, root.Id), Token);
            return async () => Assert.Equal(HttpStatusCode.NoContent, (await Api.DeleteFolderAsync(root.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        }

        var operation = await SetupAsync();
        ResetCounters();
        await operation();
        var counts = Measure();
        Assert.Equal(BulkFolders + 2, counts.VaultReads);
        Assert.Equal((int)Math.Ceiling(2 * (BulkSecrets + BulkFolders) / (double)BatchActionLimit), counts.VaultWrites);
        Assert.Equal(0, counts.Unwraps);
        Assert.Equal(0, counts.Wraps);
        Assert.Equal(BulkSecrets, (await VaultAuditRowsAsync(AuditEventType.SecretDeleted)).Count);
        AssertNoTableScan();

        await MeasureWithSetupAsync(nameof(DeleteFolderDeleteContents_10Folders500Secrets_Under5Seconds), BulkBudget, SetupAsync, HeavyWarmIterations, HeavyMeasuredIterations);
    }

    [Fact]
    public async Task RestoreFolder_10Folders500Secrets_Under5Seconds()
    {
        await WarmUpAsync(Caller);

        async Task<Func<Task>> SetupAsync()
        {
            var root = await NewFolderAsync("Bulk");
            await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, BulkSecrets, BulkFolders - 1, new SeedPlacement(root.Id, root.Id), Token);
            Assert.Equal(HttpStatusCode.NoContent, (await Api.DeleteFolderAsync(root.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
            return async () => AssertOk(await Api.RestoreRecycledFolderAsync(root.Id));
        }

        var operation = await SetupAsync();
        ResetCounters();
        await operation();
        var counts = Measure();
        Assert.Equal(4, counts.VaultReads);
        Assert.Equal((int)Math.Ceiling(2 * (BulkSecrets + BulkFolders) / (double)BatchActionLimit), counts.VaultWrites);
        Assert.Equal(1, counts.Unwraps);
        AssertNoTableScan();

        await MeasureWithSetupAsync(nameof(RestoreFolder_10Folders500Secrets_Under5Seconds), BulkBudget, SetupAsync, HeavyWarmIterations, HeavyMeasuredIterations);
    }

    [Fact]
    public async Task PurgeRun_1000ExpiredItemsAcross10Users_Under30Seconds()
    {
        var users = Enumerable.Range(0, PurgeUsers)
            .Select(index => new TestUser(new Guid(0x5e000000 + index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), $"Purge user {index}", TestTenant.Id, [UserRole]))
            .ToList();
        foreach (var user in users)
        {
            using var client = Factory.CreateAuthenticatedClient(user);
            var api = new VaultTestApi(client);
            (await client.GetAsync("/api/me", Token)).EnsureSuccessStatusCode();
            var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Expired" }));
            await VaultSeeder.SeedAsync(Factory, user.ObjectId, PurgeSecretsPerUser, 0, new SeedPlacement(null, folder.Id), Token);
            Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        }

        using var host = CreateTimedFactory(_time, TimeSpan.Zero);
        var purger = host.Services.GetRequiredService<IRecycleBinPurger>();
        _time.Advance(VaultLimits.RecycleBinRetention + TimeSpan.FromDays(1));
        ResetCounters();

        var started = Stopwatch.GetTimestamp();
        var result = await purger.PurgeExpiredAsync(Token);
        var elapsed = Stopwatch.GetElapsedTime(started);

        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PurgeRun: {elapsed.TotalMilliseconds:F0} ms for {result.SecretsPurged + result.FoldersPurged} items, budget {PurgeBudget.TotalMilliseconds:F0} ms"));
        Assert.False(result.Skipped);
        Assert.Equal(PurgeUsers * PurgeSecretsPerUser, result.SecretsPurged);
        Assert.Equal(PurgeUsers, result.FoldersPurged);
        Assert.True(elapsed < PurgeBudget, string.Create(CultureInfo.InvariantCulture, $"Purge run took {elapsed.TotalSeconds:F1} s, budget {PurgeBudget.TotalSeconds:F0} s"));
        var erased = PurgeUsers * (PurgeSecretsPerUser + 1);
        var writes = StorageRequests.Requests.Where(request => request.Method != "GET").ToList();
        var batches = writes.Count(request => BatchPath().IsMatch(request.Path));
        var leaseWrites = writes.Count(request => MaintenancePath().IsMatch(request.Path));
        TestContext.Current.TestOutputHelper?.WriteLine($"PurgeWrites: total {writes.Count}, batches {batches}, audit {StorageRequests.AuditWrites().Count}, lease {leaseWrites}");
        Assert.Equal(erased, batches);
        var auditRows = (PurgeUsers * PurgeSecretsPerUser * 2) + PurgeUsers;
        Assert.Equal(auditRows, StorageRequests.AuditWrites().Count);
        Assert.Equal(PurgeLeaseWrites, leaseWrites);
        Assert.Equal(erased + auditRows + PurgeLeaseWrites, writes.Count);
        AssertNoTableScan();
        foreach (var user in users)
        {
            using var client = Factory.CreateAuthenticatedClient(user);
            Assert.Empty(Require(await new VaultTestApi(client).ListRecycleBinAsync(pageSize: 200)).Items);
        }
    }

    [Fact]
    public async Task TwentyParallelReveals_AllUnder1Second()
    {
        await WarmUpAsync(Caller);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(Api, markers);
        var request = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };
        AssertOk(await Api.RevealSecretAsync(secret.Id, request));
        ResetCounters();

        var timings = await Task.WhenAll(Enumerable.Range(0, ParallelReveals).Select(async _ =>
        {
            var started = Stopwatch.GetTimestamp();
            var result = await Api.RevealSecretAsync(secret.Id, request);
            var elapsed = Stopwatch.GetElapsedTime(started);
            Assert.Equal(markers.Value, result.Body!.Value);
            return elapsed;
        }));

        var slowest = timings.Max();
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"TwentyParallelReveals: slowest {slowest.TotalMilliseconds:F0} ms, budget {ParallelBudget.TotalMilliseconds:F0} ms"));
        Assert.True(slowest < ParallelBudget, string.Create(CultureInfo.InvariantCulture, $"Slowest of {ParallelReveals} parallel reveals took {slowest.TotalMilliseconds:F0} ms"));
        Assert.Equal(ParallelReveals, Factory.KeyWraps.Unwraps);
        AssertNoTableScan();
        Assert.Equal(ParallelReveals + 1, (await VaultAuditRowsAsync(AuditEventType.SecretValueRevealed)).Count);
    }

    private static CreateSecretRequestDto NewRequest()
    {
        var markers = new PlaintextMarkers();
        return new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            Username = markers.Username,
            Url = markers.Url,
            Tags = [markers.Tag],
            Value = markers.Value,
            Notes = markers.Notes,
        };
    }
}
