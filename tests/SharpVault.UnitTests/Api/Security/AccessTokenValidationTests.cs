// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.Api.Security;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Api.Security;

/// <summary>
/// TEN-2 and E7 to E9: a structurally valid token is still rejected when tenant, object id, scope or identity type is wrong.
/// </summary>
[Trait("Category", "Security")]
public sealed class AccessTokenValidationTests : IDisposable
{
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose() => _logs.Dispose();

    private TokenValidatedContext ContextFor(System.Security.Claims.ClaimsPrincipal principal)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new LoggerFactory([_logs]));
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        var scheme = new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler));
        return new TokenValidatedContext(httpContext, scheme, new JwtBearerOptions()) { Principal = principal };
    }

    [Fact]
    public async Task ValidateAsync_ValidToken_DoesNotFail()
    {
        var context = ContextFor(TestIdentities.Authenticated(TestIdentities.AccessTokenClaims()));

        await AccessTokenValidation.ValidateAsync(context, TestIdentities.Settings);

        Assert.Null(context.Result);
        Assert.Empty(_logs.Messages);
    }

    [Fact]
    public async Task ValidateAsync_ForeignTenant_FailsWithConstantMessageAndLogsReasonOnly()
    {
        var foreignTenant = TestIdentities.OtherTenantId.ToString("D");
        var claims = TestIdentities.AccessTokenClaims().Replacing("tid", foreignTenant).With("name", "Alice Example").With("email", "alice@example.test");
        var context = ContextFor(TestIdentities.Authenticated(claims));

        await AccessTokenValidation.ValidateAsync(context, TestIdentities.Settings);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
        Assert.Equal("The access token is not accepted.", context.Result.Failure!.Message);
        var log = Assert.Single(_logs.Messages);
        Assert.Contains("WrongTenant", log, StringComparison.Ordinal);
        Assert.DoesNotContain(foreignTenant, log, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", log, StringComparison.Ordinal);
        Assert.DoesNotContain("example.test", log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tid", "missing")]
    [InlineData("oid", "missing")]
    [InlineData("scp", "missing")]
    [InlineData("idtyp", "app")]
    public async Task ValidateAsync_InvalidClaims_Fails(string claimType, string mode)
    {
        var claims = TestIdentities.AccessTokenClaims();
        if (mode == "missing")
        {
            claims.Without(claimType);
        }
        else
        {
            claims.With(claimType, mode);
        }

        var context = ContextFor(TestIdentities.Authenticated(claims));

        await AccessTokenValidation.ValidateAsync(context, TestIdentities.Settings);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
    }

    [Fact]
    public async Task ValidateAsync_NullPrincipal_Fails()
    {
        var context = ContextFor(TestIdentities.Authenticated([]));
        context.Principal = null;

        await AccessTokenValidation.ValidateAsync(context, TestIdentities.Settings);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
    }

    [Fact]
    public void ClockSkew_IsTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), AccessTokenValidation.ClockSkew);
    }
}
