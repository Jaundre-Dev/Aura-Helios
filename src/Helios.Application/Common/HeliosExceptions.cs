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

/// <summary>Valid request, but the result is no longer retained (410).</summary>
public sealed class GoneException(string message) : HeliosException(message, "result_expired");
