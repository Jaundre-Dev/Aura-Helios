using Helios.Application.Common;
using Helios.Application.Features.Products;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Helios.Api.Middleware;

/// <summary>
/// Maps expected application failures to problem details. Anything not listed here is
/// left to the default handler, because an unrecognised exception is a bug and must not
/// be dressed up as a tidy 4xx.
/// </summary>
public sealed class HeliosExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<HeliosExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is ProductInputException invalid)
        {
            // Invalid product input: nothing was recorded or charged. Field errors, no input echo.
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            var validation = new HttpValidationProblemDetails(invalid.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The request body is not valid for this product.",
            };
            validation.Extensions["code"] = "invalid_input";

            return await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                Exception = exception,
                ProblemDetails = validation
            });
        }

        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            UnauthenticatedException => (StatusCodes.Status401Unauthorized, "Not authenticated"),
            GoneException => (StatusCodes.Status410Gone, "Gone"),
            PayloadTooLargeException => (StatusCodes.Status413PayloadTooLarge, "Payload too large"),
            InsufficientCreditException => (StatusCodes.Status402PaymentRequired, "Insufficient credit"),
            BadRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            // A malformed body (bad JSON, an enum sent as the wrong type) is the caller's
            // mistake, not a server fault — return the 400 it carries, not a 500.
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Bad request"),
            _ => (0, string.Empty)
        };

        if (status == 0)
        {
            return false;
        }

        logger.LogInformation(
            "{Status} on {Method} {Path}: {Message}",
            status, context.Request.Method, context.Request.Path, exception.Message);

        context.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message
        };

        if (exception is HeliosException { Code: { } code })
        {
            problem.Extensions["code"] = code;
        }

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = problem
        });
    }
}
