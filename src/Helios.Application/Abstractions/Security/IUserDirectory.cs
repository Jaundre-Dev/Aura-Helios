namespace Helios.Application.Abstractions.Security;

/// <summary>
/// Read-only lookup of sign-in accounts. Application code never touches the Identity user type;
/// it resolves people through this seam so team management can name and find members.
/// </summary>
public interface IUserDirectory
{
    /// <summary>An active account by email, or null. Inactive accounts are not discoverable.</summary>
    Task<UserSummary?> FindActiveByEmailAsync(string email, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);
}

/// <param name="EmailVerified">The person proved control of the address (verification link or password reset).</param>
public sealed record UserSummary(Guid Id, string? Email, string? DisplayName, bool IsActive, bool EmailVerified = false);
