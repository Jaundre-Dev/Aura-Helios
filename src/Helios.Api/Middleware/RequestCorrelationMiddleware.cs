using System.Text.RegularExpressions;

namespace Helios.Api.Middleware;

/// <summary>
/// Gives every request one identifier, echoed in the <c>X-Request-Id</c> response header, put on
/// problem details, and recorded as the audit correlation id. A caller-supplied id is accepted
/// only if it is short and made of safe characters, so it cannot inject into logs or headers.
/// </summary>
public sealed partial class RequestCorrelationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Request-Id";

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var supplied) &&
            supplied.Count == 1 &&
            SafeId().IsMatch(supplied[0]!))
        {
            context.TraceIdentifier = supplied[0]!;
        }
        else
        {
            context.TraceIdentifier = $"req_{Guid.CreateVersion7():N}";
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = context.TraceIdentifier;
            return Task.CompletedTask;
        });

        return next(context);
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeId();
}
