namespace Helios.Contracts.Identity;

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(Guid UserId, string Token, string NewPassword);

/// <summary>
/// Returned by login instead of a session when the account has an authenticator: exchange the
/// short-lived <see cref="MfaToken"/> and a code at <c>POST /api/v1/auth/mfa/login</c>.
/// </summary>
public sealed record MfaChallengeResponse(bool MfaRequired, string MfaToken, DateTimeOffset ExpiresAt);

/// <param name="Code">A 6-digit authenticator code, or a one-time recovery code.</param>
public sealed record MfaLoginRequest(string MfaToken, string Code);

/// <param name="Code">A 6-digit authenticator code, or (where accepted) a one-time recovery code.</param>
public sealed record AccountCodeRequest(string Code);

/// <summary>Shown once, at enrolment.</summary>
public sealed record MfaSetupResponse(string Secret, string OtpAuthUri);

/// <summary>One-time recovery codes. Shown once; only hashes are kept.</summary>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record AccountSecurityResponse(bool EmailVerified, bool MfaEnabled, int RecoveryCodesRemaining);
