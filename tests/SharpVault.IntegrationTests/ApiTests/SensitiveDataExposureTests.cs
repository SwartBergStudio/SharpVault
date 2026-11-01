// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// E12 and AUD-3: user names, emails and tokens never reach the logs, error bodies or audit entries, for every kind of caller.
/// </summary>
[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SensitiveDataExposureTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private static readonly string[] Paths = [MePath, AdminUsersPath, UnknownPath];

    [Fact]
    public async Task AllCallers_Requests_NamesEmailsAndTokensAreNotLoggedOrReturnedInErrors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var unique = Guid.NewGuid().ToString("N");
        var callers = new[]
        {
            TestUsers.User, TestUsers.OtherUser, TestUsers.Admin, TestUsers.Guest, TestUsers.AuthenticatedWithoutRole,
            TestUsers.OtherTenantUser, TestUsers.LockedUser, TestUsers.LockedAdmin,
        }.Select((user, index) => user with { DisplayName = $"name-{index}-{unique}", Email = $"mail-{index}-{unique}@sharpvault.test" }).ToList();
        await Factory.SeedUserAsync(callers[6], UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        await Factory.SeedUserAsync(callers[7], UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        var tokens = new List<string>();
        Logs.Clear();

        foreach (var caller in callers)
        {
            var token = TestTokens.Create(caller);
            tokens.Add(token);
            foreach (var path in Paths)
            {
                var response = await GetWithTokenAsync(path, token);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    AssertUnauthorizedOrForbiddenBodyIsClean(body, caller.DisplayName, caller.Email!, unique);
                }
                else
                {
                    AssertUnauthorizedOrForbiddenBodyIsClean(body, token);
                    if (path == MePath)
                    {
                        AssertUnauthorizedOrForbiddenBodyIsClean(body, caller.Email!);
                    }
                }
            }
        }

        Assert.NotEmpty(Logs.Entries);
        AssertLogsDoNotContain(unique);
        foreach (var token in tokens)
        {
            foreach (var segment in token.Split('.'))
            {
                AssertLogsDoNotContain(segment);
            }
        }

        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task ApiRequest_FailureWhileSignedIn_DoesNotLogNamesEmailsOrTokens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var unique = Guid.NewGuid().ToString("N");
        var user = TestUsers.User with { DisplayName = $"name-{unique}", Email = $"mail-{unique}@sharpvault.test" };
        var token = TestTokens.Create(user);
        using var broken = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLog>();
            services.AddSingleton<IAuditLog, ThrowingAuditLog>();
        }));
        using var client = CreateClientFor(broken, token);
        Logs.Clear();

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(Logs.Entries, entry => entry.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        AssertLogsDoNotContain(unique);
        foreach (var segment in token.Split('.'))
        {
            AssertLogsDoNotContain(segment);
        }
    }

    [Fact]
    public async Task SignInAudit_AndUserRow_NeverStoreTokensOrAdditionalClaims()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var marker = "claim-" + Guid.NewGuid().ToString("N");
        var token = TestTokens.For(TestUsers.User)
            .WithClaim("unwanted_claim", marker)
            .WithClaim("groups", new[] { marker })
            .WithClaim("upn", $"{marker}@sharpvault.test")
            .Build();

        var response = await GetWithTokenAsync(MePath, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        var audit = await ListAuditRowsAsync();
        foreach (var entity in audit.Append(row))
        {
            Assert.DoesNotContain(entity, pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture)?.Contains(marker, StringComparison.Ordinal) == true);
            Assert.DoesNotContain(entity, pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture)?.Contains(token[..32], StringComparison.Ordinal) == true);
        }
    }
}
