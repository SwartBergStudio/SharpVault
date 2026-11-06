// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;
using SharpVault.Api.Errors;
using SharpVault.Application.Common.Exceptions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Api.Errors;

[Trait("Category", "Security")]
public sealed class SecretDecryptionExceptionHandlerTests
{
    private const string FixedTitle = "The item could not be read.";

    private readonly StubProblemDetailsService _problemDetails = new();

    public static TheoryData<DecryptionFailureReason> Reasons() => [.. Enum.GetValues<DecryptionFailureReason>()];

    [Fact]
    public async Task TryHandle_DecryptionException_Writes500GenericProblem()
    {
        var httpContext = new DefaultHttpContext();

        var handled = await new SecretDecryptionExceptionHandler(_problemDetails)
            .TryHandleAsync(httpContext, new SecretDecryptionException(DecryptionFailureReason.AuthenticationFailed), TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        var written = Assert.Single(_problemDetails.Written);
        Assert.Same(httpContext, written.HttpContext);
        Assert.Equal(StatusCodes.Status500InternalServerError, written.ProblemDetails.Status);
        Assert.Equal(FixedTitle, written.ProblemDetails.Title);
        Assert.Null(written.ProblemDetails.Detail);
        Assert.Null(written.ProblemDetails.Type);
        Assert.Null(written.ProblemDetails.Instance);
        Assert.Empty(written.ProblemDetails.Extensions);
    }

    [Fact]
    public async Task TryHandle_OtherException_ReturnsFalseAndWritesNothing()
    {
        var httpContext = new DefaultHttpContext();

        var handled = await new SecretDecryptionExceptionHandler(_problemDetails)
            .TryHandleAsync(httpContext, new InvalidOperationException("secret detail"), TestContext.Current.CancellationToken);

        Assert.False(handled);
        Assert.Empty(_problemDetails.Written);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
    }

    [Fact]
    public async Task TryHandle_WriterDeclines_ReturnsFalse()
    {
        _problemDetails.Result = false;

        var handled = await new SecretDecryptionExceptionHandler(_problemDetails)
            .TryHandleAsync(new DefaultHttpContext(), new SecretDecryptionException(DecryptionFailureReason.KeyMismatch), TestContext.Current.CancellationToken);

        Assert.False(handled);
    }

    [Fact]
    public async Task TryHandle_EveryReason_ProducesIdenticalBody()
    {
        var bodies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reason in Enum.GetValues<DecryptionFailureReason>())
        {
            var service = new StubProblemDetailsService();
            await new SecretDecryptionExceptionHandler(service)
                .TryHandleAsync(new DefaultHttpContext(), new SecretDecryptionException(reason), TestContext.Current.CancellationToken);

            bodies.Add(System.Text.Json.JsonSerializer.Serialize(Assert.Single(service.Written).ProblemDetails));
        }

        var body = Assert.Single(bodies);
        foreach (var reason in Enum.GetNames<DecryptionFailureReason>())
        {
            Assert.DoesNotContain(reason, body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain(SecretDecryptionException.DefaultMessage, body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task TryHandle_Reason_NeverAppearsInTheProblem(DecryptionFailureReason reason)
    {
        await new SecretDecryptionExceptionHandler(_problemDetails)
            .TryHandleAsync(new DefaultHttpContext(), new SecretDecryptionException(reason), TestContext.Current.CancellationToken);

        var problem = Assert.Single(_problemDetails.Written).ProblemDetails;
        Assert.DoesNotContain(reason.ToString(), System.Text.Json.JsonSerializer.Serialize(problem), StringComparison.OrdinalIgnoreCase);
    }
}
