// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using SharpVault.Api.Errors;
using SharpVault.Application.Common.Models;
using SharpVault.Dtos.Users;

namespace SharpVault.UnitTests.Api.Errors;

public sealed class ResultHttpExtensionsTests
{
    [Fact]
    public void ToHttpResult_Success_ReturnsOkWithValue()
    {
        var dto = new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = "Alice", IsAdmin = false };

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
    public void ToHttpResult_NotFound_Returns404WithConstantTitle()
    {
        var result = Result<CurrentUserDto>.NotFound().ToHttpResult();

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("The item was not found.", problem.ProblemDetails.Title);
        Assert.Null(problem.ProblemDetails.Detail);
    }

    [Fact]
    public void ToHttpResult_Conflict_Returns409()
    {
        var result = Result<CurrentUserDto>.Conflict().ToHttpResult();

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal("The item was changed by another request. Reload and try again.", problem.ProblemDetails.Title);
        Assert.Null(problem.ProblemDetails.Detail);
    }

    [Fact]
    public void ToHttpResult_Rejected_Returns400WithDetail()
    {
        var result = Result<CurrentUserDto>.Rejected("The folder is nested too deeply.").ToHttpResult();

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal("The request could not be completed.", problem.ProblemDetails.Title);
        Assert.Equal("The folder is nested too deeply.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public void ToHttpResult_Invalid_StillReturns400WithoutDetail()
    {
        var result = Result<CurrentUserDto>.Failure("internal text").ToHttpResult();

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Null(problem.ProblemDetails.Detail);
    }

    [Fact]
    public void ToHttpResult_NotFoundForAnyId_ProducesIdenticalProblems()
    {
        var first = Assert.IsType<ProblemHttpResult>(Result<CurrentUserDto>.NotFound().ToHttpResult());
        var second = Assert.IsType<ProblemHttpResult>(Result<CurrentUserDto>.NotFound().ToHttpResult());

        Assert.Equal(
            System.Text.Json.JsonSerializer.Serialize(first.ProblemDetails),
            System.Text.Json.JsonSerializer.Serialize(second.ProblemDetails));
    }

    [Fact]
    public void ToCreatedHttpResult_Success_Returns201WithLocation()
    {
        var dto = new CurrentUserDto { UserId = Guid.NewGuid(), DisplayName = "Alice", IsAdmin = false };

        var result = Result<CurrentUserDto>.Success(dto).ToCreatedHttpResult(value => $"/api/things/{value.UserId:D}");

        var created = Assert.IsType<Created<CurrentUserDto>>(result);
        Assert.Equal($"/api/things/{dto.UserId:D}", created.Location);
        Assert.Same(dto, created.Value);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
    }

    [Fact]
    public void ToCreatedHttpResult_Failure_ReturnsTheMappedProblemAndNeverBuildsALocation()
    {
        var calls = 0;

        var result = Result<CurrentUserDto>.NotFound().ToCreatedHttpResult(_ =>
        {
            calls++;
            return "/unused";
        });

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ToNoContentHttpResult_Success_Returns204()
    {
        var result = Result<Done>.Success(Done.Value).ToNoContentHttpResult();

        var noContent = Assert.IsType<NoContent>(result);
        Assert.Equal(StatusCodes.Status204NoContent, noContent.StatusCode);
    }

    [Theory]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status409Conflict)]
    public void ToNoContentHttpResult_Failure_ReturnsTheMappedProblem(int status)
    {
        var failure = status switch
        {
            StatusCodes.Status404NotFound => Result<Done>.NotFound(),
            StatusCodes.Status409Conflict => Result<Done>.Conflict(),
            _ => Result<Done>.Rejected("Not allowed."),
        };

        var problem = Assert.IsType<ProblemHttpResult>(failure.ToNoContentHttpResult());

        Assert.Equal(status, problem.StatusCode);
    }
}
