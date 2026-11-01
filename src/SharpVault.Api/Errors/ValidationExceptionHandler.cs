// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SharpVault.Api.Errors;

public sealed class ValidationExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    private const string Title = "The request is not valid.";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ValidationException validation)
        {
            return false;
        }

        var errors = validation.Errors
            .GroupBy(failure => failure.PropertyName, failure => failure.ErrorMessage)
            .ToDictionary(group => group.Key, group => group.Distinct().ToArray());

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = Title,
            },
        });
    }
}
