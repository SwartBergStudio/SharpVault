// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpVault.UnitTests.Support;
using SharpVault.Api.Errors;

namespace SharpVault.UnitTests.Api.Errors;

public sealed class ValidationExceptionHandlerTests
{
    private readonly StubProblemDetailsService _problemDetails = new();

    [Fact]
    public async Task TryHandleAsync_ValidationException_Writes400WithMessagesOnly()
    {
        var httpContext = new DefaultHttpContext();
        var exception = new ValidationException(
        [
            new ValidationFailure("PageSize", "PageSize must be between 1 and 100.") { AttemptedValue = -5 },
            new ValidationFailure("PageSize", "PageSize must be between 1 and 100."),
            new ValidationFailure("ContinuationToken", "ContinuationToken is not valid.") { AttemptedValue = "' or 1 eq 1" },
        ]);

        var handled = await new ValidationExceptionHandler(_problemDetails).TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        var written = Assert.Single(_problemDetails.Written);
        var details = Assert.IsType<ValidationProblemDetails>(written.ProblemDetails);
        Assert.Equal(StatusCodes.Status400BadRequest, details.Status);
        Assert.Equal("The request is not valid.", details.Title);
        Assert.Equal(["PageSize must be between 1 and 100."], details.Errors["PageSize"]);
        Assert.Equal(["ContinuationToken is not valid."], details.Errors["ContinuationToken"]);
        Assert.Equal(2, details.Errors.Count);
        var text = System.Text.Json.JsonSerializer.Serialize(details);
        Assert.DoesNotContain("or 1 eq 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("-5", text, StringComparison.Ordinal);
        Assert.Null(details.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_WriterDeclines_ReturnsFalse()
    {
        _problemDetails.Result = false;

        var handled = await new ValidationExceptionHandler(_problemDetails)
            .TryHandleAsync(new DefaultHttpContext(), new ValidationException([new ValidationFailure("A", "B")]), TestContext.Current.CancellationToken);

        Assert.False(handled);
    }

    [Fact]
    public async Task TryHandleAsync_OtherException_ReturnsFalse()
    {
        var httpContext = new DefaultHttpContext();

        var handled = await new ValidationExceptionHandler(_problemDetails)
            .TryHandleAsync(httpContext, new InvalidOperationException("secret detail"), TestContext.Current.CancellationToken);

        Assert.False(handled);
        Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        Assert.Empty(_problemDetails.Written);
    }
}
