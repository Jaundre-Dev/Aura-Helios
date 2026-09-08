namespace Helios.Application.Common;

/// <summary>Base for expected failures that map to a specific HTTP status.</summary>
public abstract class HeliosException(string message) : Exception(message);

public sealed class NotFoundException(string resource, object key)
    : HeliosException($"{resource} '{key}' was not found.");

public sealed class ConflictException(string message) : HeliosException(message);

public sealed class ForbiddenException(string message) : HeliosException(message);

/// <summary>
/// No acting user on the request. Thrown rather than returning empty, because a write
/// that silently does nothing is worse than one that fails loudly.
/// </summary>
public sealed class UnauthenticatedException()
    : HeliosException("This request has no authenticated user.");
