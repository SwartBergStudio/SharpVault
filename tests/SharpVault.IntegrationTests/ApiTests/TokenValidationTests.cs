// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.IdentityModel.Tokens;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// TEN-1 and TEN-2: access token validation. Every rejected token is exactly 401 and makes no storage call.
/// </summary>
[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TokenValidationTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private const string GraphAudience = "00000003-0000-0000-c000-000000000000";
    private const string OtherScope = "User.Read";
    private const int OversizedTokenLength = 100_000;

    private static readonly TestUser Subject = TestUsers.Admin;
    private static readonly string[] AdminRoles = ["SharpVault.Admin"];
    private static readonly string[] AdminAndScopeRoles = ["SharpVault.Admin", TestApi.Scope];
    private static readonly string[] ScopeAndOtherScope = [TestApi.Scope, OtherScope];

    public static TheoryData<string> WrongAudiences() =>
    [
        Guid.NewGuid().ToString("D"),
        TestWebApp.ClientId.ToString("D"),
        GraphAudience,
        $"api://{TestApi.ClientId:D}",
        "api://sharpvault",
        string.Empty,
        TestApi.ClientId.ToString("D") + "x",
        "x" + TestApi.ClientId.ToString("D"),
        $" {TestApi.ClientId:D}",
    ];

    public static TheoryData<string> WrongIssuers()
    {
        var tenant = TestTenant.Id.ToString("D");
        return
        [
            TestIdentityProvider.Issuer(TestTenant.OtherId),
            $"https://sts.windows.net/{tenant}/",
            "https://login.microsoftonline.com/common/v2.0",
            "https://login.microsoftonline.com/organizations/v2.0",
            $"https://login.microsoftonline.com.evil.example/{tenant}/v2.0",
            $"https://login.microsoftonline.com/{tenant}/v2.0/",
            $"http://login.microsoftonline.com/{tenant}/v2.0",
            $"https://login.microsoftonline.com/{tenant.ToUpperInvariant()}/v2.0",
            $"https://login.microsoftonline.com/{tenant}/v2.0?x=1",
            $"https://evil.example/https://login.microsoftonline.com/{tenant}/v2.0",
            string.Empty,
        ];
    }

    public static TheoryData<string> TenantClaimMismatches() =>
    [
        "OtherTenantWithOurIssuer",
        "Removed",
        "TwoElementArray",
        "NotAGuid",
        "EmptyString",
        "EmptyGuid",
        "UpperCaseOfOtherTenant",
        "OurTenantWithBraces",
    ];

    public static TheoryData<string> MissingScopes() =>
    [
        "Removed",
        "OtherScope",
        "PrefixOnly",
        "UpperCase",
        "Empty",
        "Whitespace",
        "TwoScpClaims",
        "ScopeAsRole",
        "SubstringOfOther",
    ];

    public static TheoryData<string> InvalidObjectIds() =>
    [
        "Removed",
        "NotAGuid",
        "EmptyGuid",
        "TwoElementArray",
        "Braces",
        "NoHyphens",
        "Empty",
        "Number",
        "Urn",
    ];

    public static TheoryData<int> PublicKeyEncodingIndexes()
    {
        var data = new TheoryData<int>();
        for (var index = 0; index < TamperedTokens.PublicKeyEncodings().Count; index++)
        {
            data.Add(index);
        }

        return data;
    }

    [Fact]
    public async Task ApiRequest_AlgNoneToken_Returns401()
    {
        await AssertRejectedAsync(TamperedTokens.AlgNone(Subject));
        await AssertRejectedAsync(TamperedTokens.AlgNoneKeepingSignature(Subject));
        await AssertRejectedAsync(ForgedTokens.AlgNone());
    }

    [Fact]
    public async Task ApiRequest_TokenSignedWithUnknownKey_Returns401()
    {
        await AssertRejectedAsync(TamperedTokens.SignedWithFreshKey(Subject, reuseKeyId: false));
        await AssertRejectedAsync(ForgedTokens.SignedWithUnknownKey());
    }

    [Fact]
    public async Task ApiRequest_TokenWithKnownKeyIdButOtherKey_Returns401()
    {
        await AssertRejectedAsync(TamperedTokens.SignedWithFreshKey(Subject, reuseKeyId: true));
        var token = TestTokens.For(Subject).SignedWith(TestSigningKeys.OtherWithSameKeyId, SecurityAlgorithms.RsaSha256).Build();
        await AssertRejectedAsync(token);
    }

    [Theory]
    [MemberData(nameof(PublicKeyEncodingIndexes))]
    public async Task ApiRequest_HmacTokenSignedWithPublicKeyBytes_Returns401(int encodingIndex)
    {
        var keyMaterial = TamperedTokens.PublicKeyEncodings()[encodingIndex];

        await AssertRejectedAsync(TamperedTokens.HmacSignedWithKeyMaterial(Subject, keyMaterial));
    }

    [Fact]
    public async Task ApiRequest_TokenWithStrippedOrGarbageSignature_Returns401()
    {
        await AssertRejectedAsync(TamperedTokens.StrippedSignature(Subject));
        await AssertRejectedAsync(TamperedTokens.GarbageSignature(Subject));
        await AssertRejectedAsync(TamperedTokens.WithExtraSegment(Subject));
    }

    [Fact]
    public async Task ApiRequest_PayloadTamperedAfterSigning_Returns401()
    {
        var elevated = TamperedTokens.PayloadReplacedKeepingSignature(
            TestUsers.User,
            payload => payload["roles"] = new JsonArray("SharpVault.Admin"));
        var otherUser = TamperedTokens.PayloadReplacedKeepingSignature(
            TestUsers.User,
            payload => payload["oid"] = TestUsers.Admin.ObjectId.ToString("D"));
        var otherTenant = TamperedTokens.PayloadReplacedKeepingSignature(
            TestUsers.OtherTenantUser,
            payload =>
            {
                payload["tid"] = TestTenant.Id.ToString("D");
                payload["iss"] = TestIdentityProvider.Issuer(TestTenant.Id);
            });

        await AssertRejectedAsync(elevated, AdminUsersPath);
        await AssertRejectedAsync(otherUser);
        await AssertRejectedAsync(otherTenant);
    }

    [Fact]
    public async Task ApiRequest_ExpiredToken_Returns401()
    {
        var now = DateTimeOffset.UtcNow;
        var token = TestTokens.For(Subject).ExpiresAt(now.AddMinutes(-10)).NotBefore(now.AddHours(-2)).Build();

        await AssertRejectedAsync(token);
    }

    [Fact]
    public async Task ApiRequest_TokenExpiredLongerAgoThanTheClockSkew_Returns401()
    {
        var now = DateTimeOffset.UtcNow;
        var token = TestTokens.For(Subject).ExpiresAt(now.AddMinutes(-4)).NotBefore(now.AddHours(-1)).Build();

        await AssertRejectedAsync(token);
    }

    [Fact]
    public async Task ApiRequest_NotYetValidToken_Returns401()
    {
        var now = DateTimeOffset.UtcNow;
        var token = TestTokens.For(Subject).NotBefore(now.AddMinutes(10)).ExpiresAt(now.AddHours(2)).Build();

        await AssertRejectedAsync(token);
    }

    [Fact]
    public async Task ApiRequest_TokenWithoutExpiry_Returns401()
    {
        await AssertRejectedAsync(TestTokens.For(Subject).WithoutClaim("exp").Build());
    }

    [Fact]
    public async Task ApiRequest_TokenWithNonNumericExpiry_Returns401()
    {
        await AssertRejectedAsync(TestTokens.For(Subject).WithClaim("exp", "never").Build());
    }

    [Theory]
    [MemberData(nameof(WrongAudiences))]
    public async Task ApiRequest_WrongAudience_Returns401(string audience)
    {
        await AssertRejectedAsync(TestTokens.For(Subject).WithClaim("aud", audience).Build());
    }

    [Fact]
    public async Task ApiRequest_MissingAudience_Returns401()
    {
        await AssertRejectedAsync(TestTokens.For(Subject).WithoutClaim("aud").Build());
    }

    [Theory]
    [MemberData(nameof(WrongIssuers))]
    public async Task ApiRequest_WrongIssuer_Returns401(string issuer)
    {
        await AssertRejectedAsync(TestTokens.For(Subject).WithClaim("iss", issuer).Build());
    }

    [Fact]
    public async Task ApiRequest_MissingIssuer_Returns401()
    {
        await AssertRejectedAsync(TestTokens.For(Subject).WithoutClaim("iss").Build());
    }

    [Theory]
    [MemberData(nameof(TenantClaimMismatches))]
    public async Task ApiRequest_TenantClaimMismatch_Returns401(string mismatch)
    {
        var builder = TestTokens.For(Subject);
        var other = TestTenant.OtherId.ToString("D");
        _ = mismatch switch
        {
            "OtherTenantWithOurIssuer" => builder.WithClaim("tid", other),
            "Removed" => builder.WithoutClaim("tid"),
            "TwoElementArray" => builder.WithClaim("tid", new[] { TestTenant.Id.ToString("D"), other }),
            "NotAGuid" => builder.WithClaim("tid", "not-a-guid"),
            "EmptyString" => builder.WithClaim("tid", string.Empty),
            "EmptyGuid" => builder.WithClaim("tid", Guid.Empty.ToString("D")),
            "UpperCaseOfOtherTenant" => builder.WithClaim("tid", other.ToUpperInvariant()),
            "OurTenantWithBraces" => builder.WithClaim("tid", "{" + TestTenant.Id.ToString("D") + "}"),
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch), mismatch, null),
        };

        await AssertRejectedAsync(builder.Build());
    }

    [Fact]
    public async Task ApiRequest_AppOnlyToken_Returns401()
    {
        var token = TestTokens.For(Subject)
            .WithoutClaim("scp")
            .WithClaim("idtyp", "app")
            .WithClaim("roles", AdminRoles)
            .Build();

        await AssertRejectedAsync(token, AdminUsersPath);
    }

    [Fact]
    public async Task ApiRequest_AppOnlyTokenCarryingTheUserScope_Returns401()
    {
        var token = TestTokens.For(Subject).WithClaim("idtyp", "app").Build();

        await AssertRejectedAsync(token, AdminUsersPath);
    }

    [Theory]
    [MemberData(nameof(MissingScopes))]
    public async Task ApiRequest_TokenWithoutAccessAsUserScope_Returns401(string scenario)
    {
        var builder = TestTokens.For(Subject);
        _ = scenario switch
        {
            "Removed" => builder.WithoutClaim("scp"),
            "OtherScope" => builder.WithClaim("scp", OtherScope),
            "PrefixOnly" => builder.WithClaim("scp", "access_as_user_admin"),
            "UpperCase" => builder.WithClaim("scp", "ACCESS_AS_USER"),
            "Empty" => builder.WithClaim("scp", string.Empty),
            "Whitespace" => builder.WithClaim("scp", "   "),
            "TwoScpClaims" => builder.WithClaim("scp", ScopeAndOtherScope),
            "ScopeAsRole" => builder.WithoutClaim("scp").WithClaim("roles", AdminAndScopeRoles),
            "SubstringOfOther" => builder.WithClaim("scp", "not_access_as_user"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        await AssertRejectedAsync(builder.Build());
    }

    [Theory]
    [MemberData(nameof(InvalidObjectIds))]
    public async Task ApiRequest_InvalidObjectId_Returns401(string scenario)
    {
        var builder = TestTokens.For(Subject);
        var valid = Subject.ObjectId.ToString("D");
        _ = scenario switch
        {
            "Removed" => builder.WithoutClaim("oid"),
            "NotAGuid" => builder.WithClaim("oid", "abc"),
            "EmptyGuid" => builder.WithClaim("oid", Guid.Empty.ToString("D")),
            "TwoElementArray" => builder.WithClaim("oid", new[] { valid, Guid.NewGuid().ToString("D") }),
            "Braces" => builder.WithClaim("oid", "{" + valid + "}"),
            "NoHyphens" => builder.WithClaim("oid", Subject.ObjectId.ToString("N")),
            "Empty" => builder.WithClaim("oid", string.Empty),
            "Number" => builder.WithClaim("oid", 1),
            "Urn" => builder.WithClaim("oid", "urn:uuid:" + valid),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        await AssertRejectedAsync(builder.Build());
    }

    [Theory]
    [InlineData(" ", "")]
    [InlineData("", " ")]
    [InlineData("\t", "\t")]
    [InlineData("\u00a0", "")]
    public async Task ApiRequest_WhitespacePaddedObjectId_Returns401(string prefix, string suffix)
    {
        var padded = prefix + Subject.ObjectId.ToString("D") + suffix;

        await AssertRejectedAsync(TestTokens.For(Subject).WithClaim("oid", padded).Build());
    }

    [Theory]
    [InlineData("QueryString")]
    [InlineData("Cookie")]
    [InlineData("CustomHeader")]
    [InlineData("FormBody")]
    public async Task ApiRequest_TokenOutsideTheAuthorizationHeader_Returns401(string location)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = TestTokens.Create(Subject);
        var request = location switch
        {
            "QueryString" => new HttpRequestMessage(HttpMethod.Get, $"{MePath}?access_token={Escape(token)}"),
            "Cookie" => WithHeader(new HttpRequestMessage(HttpMethod.Get, MePath), "Cookie", $"access_token={token}"),
            "CustomHeader" => WithHeader(new HttpRequestMessage(HttpMethod.Get, MePath), "X-Access-Token", token),
            "FormBody" => new HttpRequestMessage(HttpMethod.Get, MePath)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["access_token"] = token }),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(location), location, null),
        };

        var response = await CreateUnauthenticatedClient().SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [InlineData("Basic", "dXNlcjpwYXNz")]
    [InlineData("Negotiate", "YII=")]
    [InlineData("Bearer", "")]
    [InlineData("Bearer", " ")]
    [InlineData("Bearer", "not.a.jwt")]
    [InlineData("Bearer", "a.b")]
    [InlineData("Bearer", "....")]
    [InlineData("Token", "abc")]
    public async Task ApiRequest_UnsupportedOrMalformedCredentials_Returns401(string scheme, string parameter)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        request.Headers.TryAddWithoutValidation("Authorization", $"{scheme} {parameter}".TrimEnd());

        var response = await CreateUnauthenticatedClient().SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public async Task ApiRequest_OversizedGarbageToken_Returns401()
    {
        await AssertRejectedAsync(new string('a', OversizedTokenLength));
        await AssertRejectedAsync(string.Join('.', new string('a', OversizedTokenLength), new string('b', OversizedTokenLength), "c"));
    }

    [Fact]
    public async Task ApiRequest_RejectedToken_ChallengeIsBareBearerAndBodyHasNoDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var forbiddenFragments = new[]
        {
            "IDX",
            TestSigningKeys.KeyId,
            "login.microsoftonline",
            "sts.windows.net",
            TestTenant.Id.ToString("D"),
            TestTenant.OtherId.ToString("D"),
            TestApi.ClientId.ToString("D"),
            "invalid_token",
            "error_description",
            "Exception",
            "Microsoft.",
        };

        foreach (var token in RejectedTokens())
        {
            var response = await GetWithTokenAsync(MePath, token);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", response.HeaderValue("WWW-Authenticate"));
            Assert.Equal(ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);
            AssertUnauthorizedOrForbiddenBodyIsClean(body, forbiddenFragments);
            AssertUnauthorizedOrForbiddenBodyIsClean(response.Headers.ToString(), forbiddenFragments);
        }
    }

    [Fact]
    public async Task ApiRequest_AllRejectionReasons_ProduceIdenticalResponses()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var anonymous = await ErrorResponseShape.CaptureAsync(await CreateUnauthenticatedClient().GetAsync(MePath, cancellationToken), cancellationToken);

        foreach (var token in RejectedTokens())
        {
            var response = await GetWithTokenAsync(MePath, token);

            var shape = await ErrorResponseShape.CaptureAsync(response, cancellationToken);
            Assert.Equal(anonymous, shape);
        }
    }

    [Fact]
    public async Task ApiRequest_RejectedTokens_TokenNotLogged()
    {
        foreach (var token in RejectedTokens())
        {
            Logs.Clear();

            await GetWithTokenAsync(MePath, token);

            foreach (var segment in token.Split('.').Where(segment => segment.Length >= 16))
            {
                AssertLogsDoNotContain(segment);
            }
        }
    }

    [Fact]
    public async Task ApiRequest_RejectedTokens_LogOnlyReasonNotClaimValues()
    {
        var token = TestTokens.For(Subject).WithClaim("tid", TestTenant.OtherId.ToString("D")).Build();

        await GetWithTokenAsync(MePath, token);

        AssertLogsDoNotContain(TestTenant.OtherId.ToString("D"), Subject.ObjectId.ToString("D"), Subject.DisplayName, Subject.Email!);
    }

    [Fact]
    public async Task ApiRequest_ValidToken_TokenNotLogged()
    {
        var token = TestTokens.Create(TestUsers.User);

        var response = await GetWithTokenAsync(MePath, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var segment in token.Split('.'))
        {
            AssertLogsDoNotContain(segment);
        }

        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task ApiRequest_TokenForRejectedRequest_IsNotReflectedInAnyResponseHeader()
    {
        var token = TestTokens.For(Subject).WithClaim("aud", Guid.NewGuid().ToString("D")).Build();

        var response = await GetWithTokenAsync(MePath, token);

        foreach (var segment in token.Split('.'))
        {
            Assert.DoesNotContain(segment, response.Headers.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ApiRequest_LowercaseBearerScheme_IsAcceptedForValidToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        request.Headers.TryAddWithoutValidation("Authorization", "bearer " + TestTokens.Create(TestUsers.User));

        var response = await CreateUnauthenticatedClient().SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_ValidTokenWithAdditionalCredentialsInAnotherHeader_StillUsesTheBearerToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, MePath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.User));
        SpoofedTestHeaders.Add(request.Headers, TestUsers.Admin);

        var response = await CreateUnauthenticatedClient().SendAsync(request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(TestUsers.User.ObjectId.ToString("D"), body, StringComparison.Ordinal);
        Assert.DoesNotContain(TestUsers.Admin.ObjectId.ToString("D"), body, StringComparison.Ordinal);
    }

    private static HttpRequestMessage WithHeader(HttpRequestMessage request, string name, string value)
    {
        request.Headers.TryAddWithoutValidation(name, value);
        return request;
    }

    private static IReadOnlyList<string> RejectedTokens()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            TamperedTokens.AlgNone(Subject),
            TamperedTokens.StrippedSignature(Subject),
            TamperedTokens.GarbageSignature(Subject),
            TamperedTokens.SignedWithFreshKey(Subject, reuseKeyId: false),
            TamperedTokens.SignedWithFreshKey(Subject, reuseKeyId: true),
            TamperedTokens.HmacSignedWithKeyMaterial(Subject, TamperedTokens.PublicKeyEncodings()[0]),
            ForgedTokens.AlgNone(),
            ForgedTokens.SignedWithUnknownKey(),
            TestTokens.For(Subject).ExpiresAt(now.AddMinutes(-10)).NotBefore(now.AddHours(-2)).Build(),
            TestTokens.For(Subject).NotBefore(now.AddMinutes(10)).ExpiresAt(now.AddHours(2)).Build(),
            TestTokens.For(Subject).WithClaim("aud", Guid.NewGuid().ToString("D")).Build(),
            TestTokens.For(Subject).WithClaim("iss", TestIdentityProvider.Issuer(TestTenant.OtherId)).Build(),
            TestTokens.For(Subject).WithClaim("tid", TestTenant.OtherId.ToString("D")).Build(),
            TestTokens.For(Subject).WithoutClaim("scp").WithClaim("idtyp", "app").Build(),
            TestTokens.For(Subject).WithClaim("scp", OtherScope).Build(),
            TestTokens.For(Subject).WithClaim("oid", Guid.Empty.ToString("D")).Build(),
            TestTokens.Create(TestUsers.OtherTenantUser),
        ];
    }

    private async Task AssertRejectedAsync(string token, string path = MePath)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        StorageRequests.Clear();

        var response = await GetWithTokenAsync(path, token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
        Assert.Empty(await ListAuditRowsAsync());
        Assert.Equal(0, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }
}
