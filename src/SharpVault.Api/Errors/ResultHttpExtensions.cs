// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Http.HttpResults;
using SharpVault.Application.Common.Models;

namespace SharpVault.Api.Errors;

public static class ResultHttpExtensions
{
    private const string FailureTitle = "The request could not be completed.";

    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : ToProblem(result);
    }

    public static IResult ToCreatedHttpResult<T>(this Result<T> result, Func<T, string> location)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(location);
        return result.IsSuccess ? TypedResults.Created(location(result.Value!), result.Value) : ToProblem(result);
    }

    public static IResult ToNoContentHttpResult(this Result<Done> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? TypedResults.NoContent() : ToProblem(result);
    }

    private static ProblemHttpResult ToProblem<T>(Result<T> result) => result.FailureKind switch
    {
        ResultFailureKind.Rejected => TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: FailureTitle,
            detail: result.Error),
        ResultFailureKind.NotFound => TypedResults.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: ResultMessages.NotFound),
        ResultFailureKind.Forbidden => TypedResults.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: ResultMessages.Forbidden),
        ResultFailureKind.Conflict => TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: ResultMessages.Conflict),
        _ => TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: FailureTitle),
    };
}
