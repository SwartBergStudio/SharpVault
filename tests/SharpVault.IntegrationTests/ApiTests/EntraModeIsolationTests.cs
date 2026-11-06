// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Api.Security;
using SharpVault.Authentication.Local;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class EntraModeIsolationTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    [Fact]
    public async Task EntraModeApi_LocalSignedAdminToken_Returns401()
    {
        var key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength));
        var token = LocalTokens.Builder(LocalAccounts.AdminOne, key).Build();

        foreach (var path in new[] { MePath, AdminUsersPath, UnknownPath })
        {
            var response = await GetWithTokenAsync(path, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task EntraModeApi_Hs256TokenKeyedWithAnEntraKeyByte_Returns401()
    {
        var parameters = TestSigningKeys.Primary.Rsa.ExportParameters(includePrivateParameters: false);
        var token = TamperedTokens.HmacSignedWithKeyMaterial(TestUsers.Admin, parameters.Modulus!);

        foreach (var path in new[] { MePath, AdminUsersPath })
        {
            var response = await GetWithTokenAsync(path, token);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public void EntraModeApi_JwtBearerOptions_AllowOnlyRs256()
    {
        var options = Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal([SecurityAlgorithms.RsaSha256], options.TokenValidationParameters.ValidAlgorithms);
        Assert.True(options.TokenValidationParameters.RequireSignedTokens);
    }

    [Fact]
    public async Task EntraModeApi_Startup_NeverReadsOrCreatesTheKeyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "sharpvault-isolation-" + Guid.NewGuid().ToString("N"), "dev-auth-key");
        using var derived = Factory.WithWebHostBuilder(builder => builder.UseSetting("LocalAuth:KeyFilePath", path));

        using var client = CreateClientFor(derived);
        await client.GetAsync("/health", TestContext.Current.CancellationToken);
        await client.GetAsync(MePath, TestContext.Current.CancellationToken);

        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void EntraModeApi_Services_DoNotContainLocalSigningKeyIssuerOrSeeder()
    {
        var services = Factory.Services;

        Assert.Null(services.GetService<LocalSigningKey>());
        Assert.Null(services.GetService<ILocalTokenIssuer>());
        Assert.DoesNotContain(services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(), service => service is LocalAccountSeeder);
    }

    [Fact]
    public async Task EntraModeApi_Startup_InsertsNoUsers()
    {
        StorageRequests.Clear();
        using var derived = Factory.WithWebHostBuilder(_ => { });

        using var client = CreateClientFor(derived);
        await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(StorageRequests.Requests, request => request.Method == "POST" && request.Path.EndsWith("/Users", StringComparison.Ordinal));
    }

    [Fact]
    public void ApiTestFactory_BuiltHost_IsInEntraMode()
    {
        EntraModeAssertion.Assert(Factory.Services);
    }

    [Fact]
    public void EntraModeAssertion_LocalHost_Throws()
    {
        var local = Factory.Services;

        Assert.Throws<InvalidOperationException>(() => EntraModeAssertion.AssertLocal(local));
    }
}
