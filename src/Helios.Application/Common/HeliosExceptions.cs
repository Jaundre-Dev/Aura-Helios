namespace Helios.Application.Common;

/// <summary>
/// Base for expected failures that map to a specific HTTP status. <see cref="Code"/> is a stable,
/// machine-readable reason surfaced on the problem response, so API clients can branch on it
/// without parsing English.
/// </summary>
public abstract class HeliosException(string message, string? code = null) : Exception(message)
{
    public string? Code { get; } = code;
}

public sealed class NotFoundException(string resource, object key)
    : HeliosException($"{resource} '{key}' was not found.", "not_found");

public sealed class ConflictException(string message, string? code = null) : HeliosException(message, code ?? "conflict");

public sealed class ForbiddenException(string message, string? code = null) : HeliosException(message, code ?? "forbidden");

/// <summary>
/// No acting user on the request. Thrown rather than returning empty, because a write
/// that silently does nothing is worse than one that fails loudly.
/// </summary>
public sealed class UnauthenticatedException()
    : HeliosException("This request has no authenticated user.", "unauthenticated");

/// <summary>The request body exceeds the product version's bound (413). Nothing is processed.</summary>
public sealed class PayloadTooLargeException(int limitBytes)
    : HeliosException($"The request body exceeds this product's {limitBytes}-byte limit.", "input_too_large");

/// <summary>Available credit does not cover the request's maximum cost (402). Nothing is reserved or run.</summary>
public sealed class InsufficientCreditException(decimal available, decimal required)
    : HeliosException($"Available credit (R{available:0.00####}) does not cover this request's maximum cost (R{required:0.00####}).", "insufficient_credit");

/// <summary>A client error that is not a field validation failure (400), with a machine-readable code.</summary>
public sealed class BadRequestException(string message, string code) : HeliosException(message, code);

/// <summary>A dependency HELIOS needs is not available or not configured (503). Nothing was changed.</summary>
public sealed class ServiceUnavailableException(string message, string code) : HeliosException(message, code);

/// <summary>Valid request, but the result is no longer retained (410).</summary>
public sealed class GoneException(string message) : HeliosException(message, "result_expired");
