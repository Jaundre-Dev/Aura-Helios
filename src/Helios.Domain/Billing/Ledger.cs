using Helios.Contracts.Billing;
using Helios.Domain.Common;

namespace Helios.Domain.Billing;

/// <summary>
/// One ledger account. <see cref="Balance"/> is a projection of the account's entries, updated in
/// the same transaction as every posting while the row is locked; the entries remain the truth
/// and the two are reconcilable at any time. Platform accounts use <see cref="PlatformOwner"/>.
/// </summary>
public class LedgerAccount : Entity
{
    /// <summary>Owner id for platform-level accounts (revenue, gateway clearing, adjustments).</summary>
    public static readonly Guid PlatformOwner = Guid.Empty;

    public Guid OrganizationId { get; set; }
    public LedgerAccountType Type { get; set; }
    public required string Currency { get; set; }

    /// <summary>Credit-positive running balance, fixed precision.</summary>
    public decimal Balance { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// An immutable, balanced posting. <see cref="PostingKey"/> is unique, which makes every posting
/// idempotent: a second attempt to reserve, settle, release or credit the same thing is a no-op.
/// Mistakes are corrected by a reversing transaction, never by editing or deleting.
/// </summary>
public class LedgerTransaction : Entity
{
    public LedgerTransactionType Type { get; set; }
    public required string PostingKey { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? ApiRequestId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ReversesTransactionId { get; set; }
    public required string Description { get; set; }
    public required string Currency { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }

    public List<LedgerEntry> Entries { get; set; } = [];
}

/// <summary>One leg of a transaction. Signed: credit positive, debit negative. Legs sum to zero.</summary>
public class LedgerEntry : Entity
{
    public Guid TransactionId { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// Money held for one request. Ends exactly once, either settled (charged, remainder returned)
/// or released (all returned); the state change and its posting commit together.
/// </summary>
public class Reservation : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid ApiRequestId { get; set; }
    public decimal Amount { get; set; }
    public required string Currency { get; set; }
    public ReservationState State { get; set; } = ReservationState.Held;
    public decimal? SettledAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
