namespace Helios.Application.Abstractions.Security;

/// <summary>
/// Provider credentials are resolved through this, never read from configuration
/// by an application module and never persisted alongside domain rows.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(string reference, CancellationToken cancellationToken);

    Task SetAsync(string reference, string value, CancellationToken cancellationToken);
}
