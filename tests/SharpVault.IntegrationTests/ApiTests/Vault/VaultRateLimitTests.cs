// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Encryption;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultRateLimitTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private const int FloodRequests = 10;
    private const int GroupLimit = 3;

    private WebApplicationFactory<Program> Limited(int requestsPerMinute, int revealsPerMinute) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("VaultRateLimits:RequestsPerMinute", requestsPerMinute.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("VaultRateLimits:RevealsPerMinute", revealsPerMinute.ToString(CultureInfo.InvariantCulture));
        });

    private static VaultTestApi ApiOn(WebApplicationFactory<Program> host, TestUser user) =>
        new(CreateClientFor(host, TestTokens.Create(user)));

    private static VaultTestApi AnonymousOn(WebApplicationFactory<Program> host) => new(CreateClientFor(host));

    private static async Task AssertProblem429Async(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var retryAfter = response.HeaderValue("Retry-After");
        Assert.NotNull(retryAfter);
        Assert.True(int.TryParse(retryAfter, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds), $"Retry-After was '{retryAfter}'.");
        Assert.InRange(seconds, 1, 60);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("detail", out _));
        Assert.Equal("no-store", response.HeaderValue("Cache-Control"));
    }

    [Fact]
    public async Task VaultRequests_OverGroupLimit_Return429WithRetryAfterAndProblemBody()
    {
        await using var host = Limited(5, 100);
        var api = ApiOn(host, TestUsers.User);

        for (var request = 0; request < 5; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.ListFoldersAsync()).Response.StatusCode);
        }

        var limited = (await api.ListFoldersAsync()).Response;

        await AssertProblem429Async(limited);
        Assert.DoesNotContain(TestUsers.User.ObjectId.ToString("D"), await limited.Content.ReadAsStringAsync(Token), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VaultRequests_OverGroupLimit_AllGroupRoutesShareTheCounter()
    {
        await using var host = Limited(4, 100);
        var api = ApiOn(host, TestUsers.User);
        for (var request = 0; request < 4; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.ListFoldersAsync()).Response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.ListRecycleBinAsync()).Response.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.ListSecretsAsync()).Response.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "limited" })).Response.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.GetSecretAsync(Guid.NewGuid())).Response.StatusCode);
    }

    [Fact]
    public async Task VaultRequests_OverGroupLimit_RoutesOutsideTheGroupAreNotLimited()
    {
        await using var host = Limited(2, 100);
        var api = ApiOn(host, TestUsers.User);
        await api.ListFoldersAsync();
        await api.ListFoldersAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.ListFoldersAsync()).Response.StatusCode);

        var me = await api.Client.GetAsync("/api/me", Token);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task VaultRequests_OverGroupLimit_IsPerUserAndOtherUserUnaffected()
    {
        await using var host = Limited(2, 100);
        var owner = ApiOn(host, TestUsers.User);
        await owner.ListFoldersAsync();
        await owner.ListFoldersAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await owner.ListFoldersAsync()).Response.StatusCode);

        var other = ApiOn(host, TestUsers.OtherUser);

        Assert.Equal(HttpStatusCode.OK, (await other.ListFoldersAsync()).Response.StatusCode);
    }

    [Fact]
    public async Task VaultRequests_Rejected_MakeNoVaultStorageCalls()
    {
        await using var host = Limited(1, 100);
        var api = ApiOn(host, TestUsers.User);
        await api.ListFoldersAsync();
        ResetCounters();

        var limited = (await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "limited" })).Response;

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Empty(StorageRequests.VaultRequests());
        Assert.Empty(StorageRequests.AuditWrites());
    }

    [Fact]
    public async Task RevealValue_OverRateLimit_Returns429AndWritesNoAuditEntry()
    {
        await using var host = Limited(100, 3);
        var api = ApiOn(host, TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        var reveal = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };
        for (var request = 0; request < 3; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.RevealSecretAsync(secret.Id, reveal)).Response.StatusCode);
        }

        ResetCounters();
        host.Services.GetRequiredService<CountingKeyWrapProvider>().Clear();
        var limited = (await api.RevealSecretAsync(secret.Id, reveal)).Response;

        await AssertProblem429Async(limited);
        Assert.Empty(StorageRequests.VaultRequests());
        Assert.Empty(StorageRequests.AuditWrites());
        Assert.Equal(0, host.Services.GetRequiredService<CountingKeyWrapProvider>().Unwraps);
        Assert.Equal(3, (await VaultAuditRowsAsync(AuditEventType.SecretValueRevealed)).Count);
    }

    [Fact]
    public async Task RevealValue_OverRateLimit_VersionRevealSharesTheRevealLimitAndGroupRoutesStillWork()
    {
        await using var host = Limited(100, 2);
        var api = ApiOn(host, TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        Require(await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, new UpdateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            ReplaceValue = true,
            Value = markers.Value + "-v2",
        }));
        var version = Assert.Single(Require(await api.ListHistoryAsync(secret.Id)).Items);
        var reveal = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.Copy };
        Assert.Equal(HttpStatusCode.OK, (await api.RevealSecretAsync(secret.Id, reveal)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.RevealSecretAsync(secret.Id, reveal)).Response.StatusCode);

        var versionReveal = (await api.RevealVersionAsync(secret.Id, version.VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Response;

        await AssertProblem429Async(versionReveal);
        Assert.Equal(HttpStatusCode.OK, (await api.ListFoldersAsync()).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetSecretAsync(secret.Id)).Response.StatusCode);
    }

    [Fact]
    public async Task RevealRateLimit_IsPerUser_OtherUserUnaffected()
    {
        await using var host = Limited(100, 2);
        var owner = ApiOn(host, TestUsers.User);
        var other = ApiOn(host, TestUsers.OtherUser);
        var ownerSecret = await CreateSecretAsync(owner, new PlaintextMarkers());
        var otherSecret = await CreateSecretAsync(other, new PlaintextMarkers());
        var reveal = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };
        await owner.RevealSecretAsync(ownerSecret.Id, reveal);
        await owner.RevealSecretAsync(ownerSecret.Id, reveal);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await owner.RevealSecretAsync(ownerSecret.Id, reveal)).Response.StatusCode);

        var otherReveal = (await other.RevealSecretAsync(otherSecret.Id, reveal)).Response;

        Assert.Equal(HttpStatusCode.OK, otherReveal.StatusCode);
    }

    [Fact]
    public async Task RevealRoute_GroupLimitExhausted_IsStillServedByTheRevealPolicy()
    {
        await using var host = Limited(4, 10);
        var api = ApiOn(host, TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        for (var request = 0; request < 3; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.ListFoldersAsync()).Response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.ListFoldersAsync()).Response.StatusCode);

        var reveal = await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.OK, reveal.Response.StatusCode);
    }

    [Fact]
    public async Task LockedUser_NeverConsumesRateLimit()
    {
        await using var host = Limited(GroupLimit, 100);
        using var seeding = await ArrangeCallerAsync(CallerKind.LockedUser);
        var locked = ApiOn(host, TestUsers.LockedUser);

        for (var request = 0; request < FloodRequests; request++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await locked.ListFoldersAsync()).Response.StatusCode);
        }

        await SetUserStatusAsync(TestUsers.LockedUser.ObjectId, "Active");
        for (var request = 0; request < GroupLimit; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await locked.ListFoldersAsync()).Response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await locked.ListFoldersAsync()).Response.StatusCode);
    }

    [Fact]
    public async Task LockedUser_FloodingRevealRoute_NeverConsumesTheRevealLimit()
    {
        await using var host = Limited(100, 1);
        using var seeding = await ArrangeCallerAsync(CallerKind.LockedUser);
        var locked = ApiOn(host, TestUsers.LockedUser);
        var reveal = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };

        for (var request = 0; request < FloodRequests; request++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await locked.RevealSecretAsync(Guid.NewGuid(), reveal)).Response.StatusCode);
        }

        await SetUserStatusAsync(TestUsers.LockedUser.ObjectId, "Active");
        var secret = await CreateSecretAsync(locked, new PlaintextMarkers());
        Assert.Equal(HttpStatusCode.OK, (await locked.RevealSecretAsync(secret.Id, reveal)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await locked.RevealSecretAsync(secret.Id, reveal)).Response.StatusCode);
    }

    [Fact]
    public async Task RolelessCaller_NeverConsumesRateLimit()
    {
        await using var host = Limited(GroupLimit, GroupLimit);
        var roleless = ApiOn(host, TestUsers.AuthenticatedWithoutRole);

        for (var request = 0; request < FloodRequests; request++)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await roleless.ListFoldersAsync()).Response.StatusCode);
        }

        var promoted = ApiOn(host, TestUsers.AuthenticatedWithoutRole with { Roles = ["SharpVault.User"] });
        for (var request = 0; request < GroupLimit; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await promoted.ListFoldersAsync()).Response.StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await promoted.ListFoldersAsync()).Response.StatusCode);
    }

    [Fact]
    public async Task AnonymousAndForgedCallers_NeverReceive429AndNeverConsumeTheSharedPartition()
    {
        await using var host = Limited(GroupLimit, GroupLimit);
        var anonymous = AnonymousOn(host);
        var algNone = new VaultTestApi(CreateClientFor(host, ForgedTokens.AlgNone()));
        var unknownKey = new VaultTestApi(CreateClientFor(host, ForgedTokens.SignedWithUnknownKey()));

        for (var request = 0; request < FloodRequests; request++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.ListFoldersAsync()).Response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await algNone.ListFoldersAsync()).Response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await unknownKey.ListFoldersAsync()).Response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.RevealSecretAsync(Guid.NewGuid(), new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View })).Response.StatusCode);
        }

        var user = ApiOn(host, TestUsers.User);
        for (var request = 0; request < GroupLimit; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await user.ListFoldersAsync()).Response.StatusCode);
        }
    }

    [Theory]
    [InlineData("VaultRateLimits:RequestsPerMinute", "0")]
    [InlineData("VaultRateLimits:RequestsPerMinute", "100001")]
    [InlineData("VaultRateLimits:RevealsPerMinute", "0")]
    [InlineData("VaultRateLimits:RevealsPerMinute", "100001")]
    [InlineData("VaultRateLimits:ShareWritesPerMinute", "0")]
    [InlineData("VaultRateLimits:ShareWritesPerMinute", "100001")]
    [InlineData("VaultRateLimits:DirectorySearchesPerMinute", "0")]
    [InlineData("VaultRateLimits:DirectorySearchesPerMinute", "100001")]
    public void Host_OutOfRangeRateLimit_FailsToStart(string key, string value)
    {
        using var host = Factory.WithWebHostBuilder(builder => builder.UseSetting(key, value));

        var exception = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains(FlattenExceptions(exception), inner => inner is OptionsValidationException);
    }

    private static IEnumerable<Exception> FlattenExceptions(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
