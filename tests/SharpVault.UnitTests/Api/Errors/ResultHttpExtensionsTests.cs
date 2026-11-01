// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SharpVault.Api.Errors;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Users.Dtos;

namespace SharpVault.UnitTests.Api.Errors;

public sealed class ResultHttpExtensionsTests
{
    [Fact]
    public void ToHttpResult_Success_ReturnsOkWithValue()
    {
        var dto = new CurrentUserDto(Guid.NewGuid(), "Alice", false);

        var result = Result<CurrentUserDto>.Success(dto).ToHttpResult();

        var ok = Assert.IsType<Ok<CurrentUserDto>>(result);
        Assert.Same(dto, ok.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ToHttpResult_Failure_Returns400WithoutErrorText()
    {
        var result = Result<CurrentUserDto>.Failure("secret internal error for user 42").ToHttpResult();

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("The request could not be completed.", problem.ProblemDetails.Title);
        Assert.Null(problem.ProblemDetails.Detail);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(problem.ProblemDetails), StringComparison.Ordinal);
    }

    [Fact]
    public void ToHttpResult_NullResult_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ResultHttpExtensions.ToHttpResult<CurrentUserDto>(null!));
    }
}
