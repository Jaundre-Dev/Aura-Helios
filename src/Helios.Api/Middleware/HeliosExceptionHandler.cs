using Helios.Application.Common;
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
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            UnauthenticatedException => (StatusCodes.Status401Unauthorized, "Not authenticated"),
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

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message
            }
        });
    }
}
