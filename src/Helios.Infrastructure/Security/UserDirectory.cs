using Helios.Application.Abstractions.Security;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Security;

public sealed class UserDirectory(HeliosDbContext db) : IUserDirectory
{
    public async Task<UserSummary?> FindActiveByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // Identity stores the upper-invariant form for case-insensitive lookup.
        var normalised = email.Trim().ToUpperInvariant();

        return await db.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalised && u.IsActive)
            .Select(u => new UserSummary(u.Id, u.Email, u.DisplayName, u.IsActive, u.EmailConfirmed))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, UserSummary>> GetAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }

        return await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email, u.DisplayName, u.IsActive, u.EmailConfirmed))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
    }
}
