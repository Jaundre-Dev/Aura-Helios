using Helios.Domain.Common;

namespace Helios.Domain.Identity;

public enum AccountTokenPurpose
{
    EmailVerification,
    PasswordReset
}

/// <summary>
/// A single-use, expiring token sent to an account's email address. Only a SHA-256 hash is stored,
/// so a database read cannot be turned into a working link. Kept in MySQL rather than derived from
/// a per-machine key, so any API instance can redeem a token another instance issued.
/// </summary>
public class AccountToken : Entity
{
    public Guid UserId { get; set; }
    public AccountTokenPurpose Purpose { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>
/// A customer's authenticator app (RFC 6238). When confirmed, sign-in needs a code as well as the
/// password. Same replay and lockout rules as platform staff authenticators.
/// </summary>
public class UserAuthenticator : Entity
{
    public const int MaxFailedCodes = 5;
    public static readonly TimeSpan CodeLockout = TimeSpan.FromMinutes(15);

    public Guid UserId { get; set; }
    public required byte[] SecretEnvelope { get; set; }
    public required string KeyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public long? LastStep { get; set; }
    public int FailedCodeCount { get; set; }
    public DateTimeOffset? CodeLockedUntil { get; set; }
}

public enum RecoveryCodeScope
{
    /// <summary>Stands in for the customer authenticator at sign-in.</summary>
    Account,

    /// <summary>Stands in for the platform staff authenticator at platform step-up.</summary>
    Platform
}

/// <summary>A one-time code for when the authenticator is lost. Stored hashed; each works once.</summary>
public class RecoveryCode : Entity
{
    public Guid UserId { get; set; }
    public RecoveryCodeScope Scope { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
