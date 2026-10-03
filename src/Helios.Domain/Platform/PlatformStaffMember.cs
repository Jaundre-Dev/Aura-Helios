using Helios.Contracts.Platform;
using Helios.Domain.Common;

namespace Helios.Domain.Platform;

/// <summary>
/// A HELIOS employee's platform role. Held apart from customer memberships: it grants no access
/// inside any customer company, and the platform surface never returns documents or results.
/// Platform endpoints additionally require a step-up session proven with an authenticator code
/// (plan section 5: MFA for platform administrators).
/// </summary>
public class PlatformStaffMember : AuditableEntity, IAggregateRoot
{
    public const int MaxFailedCodes = 5;
    public static readonly TimeSpan CodeLockout = TimeSpan.FromMinutes(15);

    public Guid UserId { get; set; }
    public PlatformRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>The TOTP shared secret, sealed under the server keyring. Null until enrolment.</summary>
    public byte[]? TotpSecretEnvelope { get; set; }
    public string? TotpKeyId { get; set; }

    /// <summary>Set when the first code proves the authenticator was set up correctly.</summary>
    public DateTimeOffset? TotpConfirmedAt { get; set; }

    /// <summary>
    /// The highest TOTP time step ever accepted. A code is accepted only for a later step, so an
    /// observed code cannot be replayed, even within its validity window.
    /// </summary>
    public long? LastTotpStep { get; set; }

    public int FailedCodeCount { get; set; }
    public DateTimeOffset? CodeLockedUntil { get; set; }

    public bool MfaConfirmed => TotpConfirmedAt is not null && TotpSecretEnvelope is not null;
}

/// <summary>
/// The platform role → permission map. Support cannot touch money, staff or approvals, so a support
/// user cannot grant themselves broader access; no role can change its own staff record.
/// </summary>
public static class PlatformPermissions
{
    private static readonly IReadOnlyDictionary<PlatformRole, HashSet<PlatformPermission>> Map =
        new Dictionary<PlatformRole, HashSet<PlatformPermission>>
        {
            [PlatformRole.Administrator] = [.. Enum.GetValues<PlatformPermission>()],

            [PlatformRole.Finance] =
            [
                PlatformPermission.PublishPrices,
                PlatformPermission.AdjustCredit,
                PlatformPermission.ResolveRequests,
                PlatformPermission.ViewOperations,
                PlatformPermission.ViewPayments,
            ],

            [PlatformRole.Support] =
            [
                PlatformPermission.ViewOperations,
                PlatformPermission.RetryDeliveries,
            ],
        };

    public static bool Grants(PlatformRole role, PlatformPermission permission) =>
        Map.TryGetValue(role, out var permissions) && permissions.Contains(permission);
}
