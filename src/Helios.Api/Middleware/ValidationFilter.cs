using FluentValidation;

namespace Helios.Api.Middleware;

/// <summary>
/// Validates the request body before the handler runs and returns RFC 9457 problem
/// details on failure, so every endpoint reports bad input the same way.
/// </summary>
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();

        if (request is null)
        {
            return Results.Problem(
                title: "Missing request body",
                detail: $"A {typeof(TRequest).Name} body is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        if (result.IsValid)
        {
            return await next(context);
        }

        // Group by property so a field with two broken rules reports both.
        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        return Results.ValidationProblem(errors, title: "One or more validation errors occurred.");
    }
}

public static class ValidationFilterExtensions
{
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class =>
        builder
            .AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
