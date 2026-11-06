// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed partial class VaultLeakageTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private const string NoStore = "no-store";
    private const string ValueEnvelope = "ValueEnvelope";
    private const string NotesEnvelope = "NotesEnvelope";

    private static readonly Guid Owner = TestUsers.User.ObjectId;

    private static readonly HashSet<string> AllowedAuditColumns = new(StringComparer.Ordinal)
    {
        "PartitionKey", "RowKey", "Timestamp", "odata.etag",
        "EventType", "ActorId", "TargetId", "OccurredAt", "IpAddress", "Reason", "OwnerId", "SecretId",
    };

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[0-9a-fA-F]{32}", RegexOptions.CultureInvariant)]
    private static partial Regex AnyId();

    [GeneratedRegex(@"^Decryption failed for item [0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12} with purpose [a-z.]+: [A-Za-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex DecryptionLogShape();

    private static string TableOf(string path)
    {
        var segment = path.TrimStart('/').Split('/').Skip(1).FirstOrDefault() ?? string.Empty;
        var paren = segment.IndexOf('(', StringComparison.Ordinal);
        return paren < 0 ? segment : segment[..paren];
    }

    private IReadOnlyList<string> CallPattern() =>
        [.. StorageRequests.Requests.Select(request => $"{request.Method} {TableOf(request.Path)}").Order(StringComparer.Ordinal)];

    private async Task<IReadOnlyList<TableEntity>> AllRowsAsync(string table)
    {
        var rows = new List<TableEntity>();
        await foreach (var row in GetTableClient(table).QueryAsync<TableEntity>(cancellationToken: Token))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static IEnumerable<string> TextsOf(TableEntity row)
    {
        foreach (var property in row)
        {
            switch (property.Value)
            {
                case string text:
                    yield return text;
                    break;
                case byte[] bytes:
                    yield return Encoding.UTF8.GetString(bytes);
                    yield return Convert.ToBase64String(bytes);
                    break;
                case null:
                    break;
                default:
                    yield return Convert.ToString(property.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    break;
            }
        }
    }

    private static bool ContainsBytes(TableEntity row, string marker)
    {
        var needle = Encoding.UTF8.GetBytes(marker);
        return row.Select(property => property.Value).OfType<byte[]>().Any(bytes => bytes.AsSpan().IndexOf(needle) >= 0);
    }

    private async Task AssertSameAsRandomIdAsync(Func<VaultTestApi, Guid, Task<HttpResponseMessage>> call, Func<VaultScenario, Guid> foreignIdOf)
    {
        var scenario = await VaultScenario.ArrangeAsync(ApiFor(TestUsers.User));
        var attacker = ApiFor(TestUsers.OtherUser);
        await CreateSecretAsync(attacker, new PlaintextMarkers());
        await WarmUpAsync(TestUsers.OtherUser);

        var foreignId = foreignIdOf(scenario);
        ResetCounters();
        var foreign = await call(attacker, foreignId);
        var foreignBody = await BodyWithoutTraceAsync(foreign);
        var foreignPattern = CallPattern();
        ResetCounters();
        var random = await call(attacker, Guid.NewGuid());
        var randomBody = await BodyWithoutTraceAsync(random);
        var randomPattern = CallPattern();

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(random.StatusCode, foreign.StatusCode);
        Assert.Equal(randomBody, foreignBody);
        Assert.Equal(randomPattern, foreignPattern);
    }

    [Fact]
    public async Task ListAndGetAndMutations_Responses_NeverContainValueOrNotesMarkers()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var responses = new List<HttpResponseMessage>();
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var created = await api.CreateSecretAsync(NewSecretRequest(markers, folder.Id));
        responses.Add(created.Response);
        responses.Add((await api.ListSecretsAsync()).Response);
        responses.Add((await api.GetSecretAsync(created.Body!.Id)).Response);
        var updated = await api.UpdateSecretAsync(created.Body.Id, created.Body.ConcurrencyToken, UpdateRequest(markers, markers.Value + "-v2"));
        responses.Add(updated.Response);
        var moved = await api.MoveSecretAsync(created.Body.Id, updated.Body!.ConcurrencyToken, new MoveSecretRequestDto { FolderId = null });
        responses.Add(moved.Response);
        var history = await api.ListHistoryAsync(created.Body.Id);
        responses.Add(history.Response);
        var restoredVersion = await api.RestoreVersionAsync(created.Body.Id, history.Body!.Items[0].VersionId, moved.Body!.ConcurrencyToken);
        responses.Add(restoredVersion.Response);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(created.Body.Id, restoredVersion.Body!.ConcurrencyToken)).StatusCode);
        responses.Add((await api.ListRecycleBinAsync()).Response);
        responses.Add((await api.RestoreRecycledSecretAsync(created.Body.Id)).Response);
        responses.Add((await api.ListFoldersAsync()).Response);

        foreach (var response in responses)
        {
            Assert.True(response.IsSuccessStatusCode, $"Unexpected status {response.StatusCode}.");
            var text = await BodyOfAsync(response);
            Assert.DoesNotContain(markers.Value, text, StringComparison.Ordinal);
            Assert.DoesNotContain(markers.Notes, text, StringComparison.Ordinal);
            Assert.DoesNotContain("\"value\"", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"notes\"", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RevealValue_Success_WritesOneAuditEntryAndOneSecretAuditRow()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());

        await RevealValueAsync(api, secret.Id);

        var entries = await VaultAuditRowsAsync(AuditEventType.SecretValueRevealed);
        var entry = Assert.Single(entries);
        Assert.Equal(secret.Id.ToString("D"), entry.GetString("TargetId"));
        Assert.Equal(Owner.ToString("D"), entry.GetString("ActorId"));
        var indexRows = (await SecretAuditRowsAsync(Owner, secret.Id)).Where(row => row.GetString("EventType") == nameof(AuditEventType.SecretValueRevealed));
        Assert.Single(indexRows);
    }

    [Fact]
    public async Task RevealValue_AuditWriteFails_Returns500WithoutValue()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        using var broken = WithThrowingAudit();

        var response = (await ApiOn(broken, TestUsers.User).RevealSecretAsync(secret.Id, ViewValue)).Response;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        markers.AssertAbsentFrom(await BodyOfAsync(response));
    }

    [Fact]
    public async Task RevealNotes_CopyIntent_WritesSecretNotesCopied()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);

        var result = await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.Copy });

        Assert.Equal(markers.Notes, result.Body!.Value);
        Assert.Single(await VaultAuditRowsAsync(AuditEventType.SecretNotesCopied));
        Assert.Empty(await VaultAuditRowsAsync(AuditEventType.SecretNotesRevealed, AuditEventType.SecretValueCopied, AuditEventType.SecretValueRevealed));
    }

    [Fact]
    public async Task TwentyParallelReveals_WriteTwentyAuditEntries()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => api.RevealSecretAsync(secret.Id, ViewValue)));

        Assert.All(results, result => Assert.Equal(markers.Value, result.Body!.Value));
        Assert.Equal(20, (await VaultAuditRowsAsync(AuditEventType.SecretValueRevealed)).Count);
        Assert.Equal(20, (await SecretAuditRowsAsync(Owner, secret.Id)).Count(row => row.GetString("EventType") == nameof(AuditEventType.SecretValueRevealed)));
    }

    [Fact]
    public async Task HistoryReveal_Success_WritesSecretVersionRevealed()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);

        var result = await api.RevealVersionAsync(scenario.SecretS, scenario.VersionV, new RevealVersionRequestDto { Intent = RevealIntentDto.View });

        Assert.Equal(scenario.Markers.Value, result.Body!.Value);
        var entry = Assert.Single(await VaultAuditRowsAsync(AuditEventType.SecretVersionRevealed));
        Assert.Equal(scenario.VersionV.ToString("D"), entry.GetString("TargetId"));
        Assert.Equal(scenario.SecretS.ToString("D"), entry.GetString("SecretId"));
        Assert.Single(await SecretAuditRowsAsync(Owner, scenario.SecretS), row => row.GetString("EventType") == nameof(AuditEventType.SecretVersionRevealed));
    }

    [Fact]
    public async Task HistoryReveal_AuditWriteFails_Returns500WithoutValue()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);
        using var broken = WithThrowingAudit();

        var response = (await ApiOn(broken, TestUsers.User).RevealVersionAsync(scenario.SecretS, scenario.VersionV, new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Response;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        scenario.Markers.AssertAbsentFrom(await BodyOfAsync(response));
        Assert.DoesNotContain(scenario.Markers.Value + "-v2", await BodyOfAsync(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VaultAuditEntries_AfterFullLifecycle_ContainNoPlaintextMarkers()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        await RunLifecycleAsync(api, markers);

        var rows = (await AllRowsAsync(TableNames.AuditLog)).Concat(await AllRowsAsync(TableNames.SecretAuditLog)).ToList();

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            foreach (var text in TextsOf(row))
            {
                markers.AssertAbsentFrom(text);
            }
        }
    }

    [Fact]
    public async Task VaultAuditEntries_HaveOnlyAllowedColumns()
    {
        await RunLifecycleAsync(ApiFor(TestUsers.User), new PlaintextMarkers());

        var rows = (await AllRowsAsync(TableNames.AuditLog)).Concat(await AllRowsAsync(TableNames.SecretAuditLog)).ToList();

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.All(row.Keys, key => Assert.Contains(key, AllowedAuditColumns));
        }
    }

    [Fact]
    public async Task VaultAuditEntries_ReasonIsAlwaysNull()
    {
        await RunLifecycleAsync(ApiFor(TestUsers.User), new PlaintextMarkers());

        var rows = (await AllRowsAsync(TableNames.AuditLog)).Concat(await AllRowsAsync(TableNames.SecretAuditLog)).ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.True(string.IsNullOrEmpty(row.GetString("Reason"))));
    }

    [Fact]
    public async Task VaultRows_AfterLifecycle_ContainNoPlaintextMarkers()
    {
        var api = ApiFor(TestUsers.User);
        var lifecycle = new PlaintextMarkers();
        await RunLifecycleAsync(api, lifecycle);
        var scenario = await VaultScenario.ArrangeAsync(api);
        var other = new PlaintextMarkers();
        await CreateSecretAsync(ApiFor(TestUsers.OtherUser), other);

        var rows = await AllRowsAsync(TableNames.Vaults);

        Assert.True(rows.Count >= 9);
        foreach (var row in rows)
        {
            foreach (var marker in lifecycle.All.Concat(scenario.Markers.All).Concat(other.All).Append(scenario.Markers.Value + "-v2"))
            {
                Assert.False(ContainsBytes(row, marker), $"The row {row.RowKey} holds the marker bytes.");
                Assert.All(TextsOf(row), text => Assert.DoesNotContain(marker, text, StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public async Task VaultLifecycle_CapturedLogsAtDebug_ContainNoPlaintextMarkersOrKeyMaterial()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        Logs.Clear();

        await RunLifecycleAsync(api, markers);
        var scenario = await VaultScenario.ArrangeAsync(api);

        var wrapped = (await AllRowsAsync(TableNames.Vaults))
            .Select(row => row.TryGetValue("WrappedDataKey", out var key) ? key as byte[] : null)
            .OfType<byte[]>()
            .Select(Convert.ToBase64String)
            .ToList();
        Assert.NotEmpty(wrapped);
        Assert.NotEmpty(Logs.Entries);
        foreach (var value in markers.All.Concat(scenario.Markers.All).Append(Factory.DevelopmentKeyMaterial).Concat(wrapped))
        {
            Assert.False(Logs.ContainsText(value), "A captured log entry contained plaintext, key material or a wrapped key.");
        }
    }

    [Fact]
    public async Task DecryptionFailure_Log_HasItemIdPurposeAndReasonOnly()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var row = await VaultRows.GetAsync(VaultsTable, Owner, VaultKeys.SecretRowKey(secret.Id, recycled: false), Token);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner, VaultKeys.SecretRowKey(secret.Id, recycled: false), ValueEnvelope, row![NotesEnvelope], Token);
        Logs.Clear();

        var response = (await api.RevealSecretAsync(secret.Id, ViewValue)).Response;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var entry = Assert.Single(Logs.Entries, candidate => candidate.Message.StartsWith("Decryption failed", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Matches(DecryptionLogShape(), entry.Message);
        Assert.Contains(secret.Id.ToString("D"), entry.Message, StringComparison.Ordinal);
        Assert.Contains("secret.value", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
        markers.AssertAbsentFrom(entry.Message);
    }

    [Fact]
    public async Task ValidationFailure_Logs_DoNotContainSubmittedValue()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        Logs.Clear();

        var oversized = await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "n", Value = markers.Value + new string('x', 9000), Notes = markers.Notes });
        var badUrl = await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = markers.Name, Url = "javascript:" + markers.Url, Username = markers.Username });
        var badFolder = await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "\u0001" });

        Assert.Equal(HttpStatusCode.BadRequest, oversized.Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badUrl.Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badFolder.Response.StatusCode);
        AssertLogsDoNotContain([.. markers.All]);
    }

    [Fact]
    public async Task ValidationErrors_OversizedValue_DoNotEchoSubmittedValue()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();

        var result = await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = markers.Name, Value = markers.Value + new string('x', 9000) });

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        var text = await BodyOfAsync(result.Response);
        markers.AssertAbsentFrom(text);
        Assert.DoesNotContain(new string('x', 50), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationErrors_InvalidUrl_DoNotEchoUrl()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var url = "javascript:alert('" + markers.Url + "')";

        var result = await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = markers.Name, Url = url });

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        var text = await BodyOfAsync(result.Response);
        markers.AssertAbsentFrom(text);
        Assert.DoesNotContain("javascript", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecryptionFailure_Response_IsGenericProblemWithoutReason()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner, VaultKeys.SecretRowKey(secret.Id, recycled: false), ValueEnvelope, new byte[] { 1, 2, 3, 4 }, Token);

        var response = (await api.RevealSecretAsync(secret.Id, ViewValue)).Response;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var text = await BodyOfAsync(response);
        foreach (var reason in Enum.GetNames<SharpVault.Application.Common.Exceptions.DecryptionFailureReason>())
        {
            Assert.DoesNotContain(reason, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(secret.Id.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at SharpVault", text, StringComparison.Ordinal);
        markers.AssertAbsentFrom(text);
    }

    [Fact]
    public async Task RejectedFolderMove_Response_HasConstantDetailOnly()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var parent = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-parent" }));
        var child = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-child", ParentId = parent.Id }));

        var intoItself = await api.MoveFolderAsync(parent.Id, new MoveFolderRequestDto { ParentId = parent.Id });
        var intoDescendant = await api.MoveFolderAsync(parent.Id, new MoveFolderRequestDto { ParentId = child.Id });

        foreach (var response in new[] { intoItself.Response, intoDescendant.Response })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var text = await BodyOfAsync(response);
            Assert.Equal(VaultMessages.FolderCycle, (await ProblemOfAsync(response)).GetProperty("detail").GetString());
            markers.AssertAbsentFrom(text);
            Assert.DoesNotContain(parent.Id.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(child.Id.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RevealValue_Response_HasNoStoreCacheControl()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());

        var response = (await api.RevealSecretAsync(secret.Id, ViewValue)).Response;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(NoStore, response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoryReveal_Response_HasNoStoreCacheControl()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);

        var response = (await api.RevealVersionAsync(scenario.SecretS, scenario.VersionV, new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Response;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(NoStore, response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevealEndpoint_Get_Returns405()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);

        var response = await api.SendRawAsync(HttpMethod.Get, VaultTestApi.Bind(ApiRoutes.VaultSecretReveal, ("secretId", secret.Id)), null, null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        markers.AssertAbsentFrom(await BodyOfAsync(response));
        Assert.Empty(await VaultAuditRowsAsync(AuditEventType.SecretValueRevealed));
    }

    [Fact]
    public async Task GetSecret_OtherUsersIdAndRandomId_ReturnIdenticalStatusBodyAndStorageCallPattern() =>
        await AssertSameAsRandomIdAsync(GetSecretCall, scenario => scenario.SecretS);

    [Fact]
    public async Task RevealSecret_OtherUsersIdAndRandomId_ReturnIdenticalStatusBodyAndStorageCallPattern() =>
        await AssertSameAsRandomIdAsync(RevealSecretCall, scenario => scenario.SecretS);

    [Fact]
    public async Task HistoryReveal_OtherUsersIdAndRandomId_ReturnIdenticalStatusBodyAndStorageCallPattern() =>
        await AssertSameAsRandomIdAsync(HistoryRevealCall, scenario => scenario.SecretS);

    [Fact]
    public async Task RecycleBinRestore_OtherUsersIdAndRandomId_ReturnIdenticalStatusBodyAndStorageCallPattern() =>
        await AssertSameAsRandomIdAsync(BinnedRestoreCall, scenario => scenario.BinnedSecretB);

    [Fact]
    public async Task VaultLifecycle_LogsAtInformationOrAbove_ContainNoVaultIds()
    {
        var api = ApiFor(TestUsers.User);
        await WarmUpAsync(TestUsers.User);
        Logs.Clear();

        await RunLifecycleAsync(api, new PlaintextMarkers());
        await VaultScenario.ArrangeAsync(api);

        var vaultEntries = Logs.Entries
            .Where(entry => entry.Level >= LogLevel.Information)
            .Where(entry => entry.Category.StartsWith("SharpVault.", StringComparison.Ordinal))
            .Where(entry => !entry.Category.EndsWith("UserAccessGate", StringComparison.Ordinal) && !entry.Category.EndsWith("SharpVaultAccessHandler", StringComparison.Ordinal))
            .ToList();
        foreach (var entry in vaultEntries)
        {
            Assert.False(AnyId().IsMatch(entry.Message), $"The log entry from {entry.Category} holds an id.");
            Assert.False(AnyId().IsMatch(entry.Exception ?? string.Empty), $"The log exception from {entry.Category} holds an id.");
        }
    }

    [Fact]
    public async Task AuditFailure_Log_HasExceptionTypeButNotExceptionText()
    {
        var markers = new PlaintextMarkers();
        using var broken = WithThrowingAudit();
        var api = ApiOn(broken, TestUsers.User);
        await WarmUpAsync(TestUsers.User);
        Logs.Clear();

        var response = (await api.CreateSecretAsync(NewSecretRequest(markers))).Response;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var entry = Assert.Single(Logs.Entries, candidate => candidate.Category.EndsWith("VaultAuditWriter", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("RequestFailedException", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Service unavailable", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Exception);
        markers.AssertAbsentFrom(entry.Message);
    }

    [Fact]
    public async Task AzureCore_LogsBelowWarning_AreNotCaptured()
    {
        await RunLifecycleAsync(ApiFor(TestUsers.User), new PlaintextMarkers());

        var azure = Logs.Entries.Where(entry => entry.Category.StartsWith("Azure", StringComparison.Ordinal)).ToList();

        Assert.DoesNotContain(azure, entry => entry.Level < LogLevel.Warning && entry.Category.StartsWith("Azure.Core", StringComparison.Ordinal));
        Assert.NotEmpty(Logs.Entries);
    }

    private static Task<HttpResponseMessage> Run(Func<Task<HttpResponseMessage>> call) => call();

    private static Task<HttpResponseMessage> GetSecretCall(VaultTestApi api, Guid id) =>
        Run(async () => (await api.GetSecretAsync(id)).Response);

    private static Task<HttpResponseMessage> RevealSecretCall(VaultTestApi api, Guid id) =>
        Run(async () => (await api.RevealSecretAsync(id, ViewValue)).Response);

    private static Task<HttpResponseMessage> HistoryRevealCall(VaultTestApi api, Guid id) =>
        Run(async () => (await api.RevealVersionAsync(id, Guid.NewGuid(), new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Response);

    private static Task<HttpResponseMessage> BinnedRestoreCall(VaultTestApi api, Guid id) =>
        Run(async () => (await api.RestoreRecycledSecretAsync(id)).Response);
}
