// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.Api.Errors;

public static class ResultHttpExtensions
{
    private const string FailureTitle = "The request could not be completed.";

    public static IResult ToHttpResult<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: FailureTitle);
    }
}
