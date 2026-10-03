using System.Security.Cryptography;
using System.Text;
using Helios.Application.Abstractions.Payments;
using Helios.Application.Abstractions.Persistence;
using Helios.Application.Abstractions.Security;
using Helios.Application.Common;
using Helios.Application.Features.Identity;
using Helios.Contracts.Billing;
using Helios.Contracts.Organizations;
using Helios.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Features.Billing;

/// <summary>What a callback did, for the gateway's HTTP response and for tests.</summary>
public enum CallbackResult
{
    /// <summary>Signature or freshness check failed: answered 401, nothing recorded or changed.</summary>
    Unauthenticated,

    Processed
}

/// <summary>
/// Prepaid top-ups (plan section 9). The ledger is credited only from a verified gateway event that
/// matches the payment's merchant, currency, amount and reference — never from the customer's return
/// redirect — and at most once per payment however many times or orders callbacks arrive.
/// </summary>
public sealed class PaymentService(
    IHeliosDbContext db,
    IUnitOfWork unitOfWork,
    IRowLocks locks,
    OrganizationAccess access,
    LedgerService ledger,
    IEnumerable<IPaymentGateway> gateways,
    IAuditWriter audit,
    TimeProvider clock)
{
    public const decimal MinimumTopUp = 10m;
    public const decimal MaximumTopUp = 100_000m;

    public async Task<PaymentResponse> CreateTopUpAsync(Guid organizationId, CreateTopUpRequest request, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ManageBilling, ct);

        if (request.Amount is < MinimumTopUp or > MaximumTopUp || decimal.Round(request.Amount, 2) != request.Amount)
        {
            throw new BadRequestException(
                $"Top-ups are between R{MinimumTopUp} and R{MaximumTopUp}, in whole cents.", "invalid_amount");
        }

        var gateway = gateways.FirstOrDefault()
            ?? throw new ServiceUnavailableException(
                "Online top-ups are not available: no approved payment gateway is configured.", "payments_unavailable");

        var payment = new Payment
        {
            OrganizationId = organizationId,
            Gateway = gateway.Name,
            Amount = request.Amount,
            Currency = LedgerService.Currency,
            CreatedBy = access.RequireUser(),
            CreatedAt = clock.GetUtcNow()
        };

        db.Payments.Add(payment);
        audit.Record("payment.create", nameof(Payment), payment.Id.ToString(), organizationId: organizationId,
            metadataJson: string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $$"""{"amount":{{payment.Amount}},"gateway":"{{gateway.Name}}"}"""));
        await db.SaveChangesAsync(ct);

        // The gateway call happens outside any transaction; the pending row already exists, so a
        // callback that races the response still finds its payment.
        try
        {
            var session = await gateway.CreateCheckoutAsync(
                new CheckoutRequest(payment.Id, organizationId, payment.Amount, payment.Currency), ct);

            payment.GatewayReference = session.GatewayReference;
            payment.CheckoutUrl = session.CheckoutUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            payment.Status = PaymentStatus.Failed;
            payment.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            throw new ServiceUnavailableException("The payment gateway could not start a checkout. Try again later.", "gateway_unavailable");
        }

        await db.SaveChangesAsync(ct);
        return ToResponse(payment);
    }

    public async Task<IReadOnlyList<PaymentResponse>> ListAsync(Guid organizationId, CancellationToken ct)
    {
        await access.RequireAsync(organizationId, OrganizationPermission.ViewBilling, ct);

        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return payments.Select(ToResponse).ToList();
    }

    /// <summary>
    /// Handles one gateway callback. Unknown gateways and unverifiable callbacks change nothing.
    /// Verified events are recorded once (unique gateway + event id) and applied under a lock on
    /// the payment row, so concurrent and repeated deliveries cannot credit twice.
    /// </summary>
    public async Task<CallbackResult> HandleCallbackAsync(
        string gatewayName,
        IReadOnlyDictionary<string, string> headers,
        string rawBody,
        CancellationToken ct)
    {
        var gateway = gateways.FirstOrDefault(g => g.Name == gatewayName)
            ?? throw new NotFoundException("Payment gateway", gatewayName);

        if (gateway.Verify(headers, rawBody) is not { } verified)
        {
            return CallbackResult.Unauthenticated;
        }

        var payloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody)));

        try
        {
            await unitOfWork.ExecuteInTransactionAsync(token => ApplyAsync(gateway, verified, payloadHash, token), ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent delivery of the same event id committed first; this one has no effect.
        }

        return CallbackResult.Processed;
    }

    private async Task<bool> ApplyAsync(IPaymentGateway gateway, VerifiedPaymentEvent verified, string payloadHash, CancellationToken ct)
    {
        if (await db.PaymentEvents.AnyAsync(e => e.Gateway == gateway.Name && e.EventId == verified.EventId, ct))
        {
            return false;
        }

        var payment = await locks.LockPaymentAsync(gateway.Name, verified.GatewayReference, ct);
        var (outcome, reason) = Decide(gateway, verified, payment);

        if (outcome == PaymentEventOutcome.Applied && payment is not null)
        {
            var now = clock.GetUtcNow();

            if (verified.Type == PaymentEventType.PaymentSucceeded)
            {
                await ledger.CreditTopUpAsync(payment.OrganizationId, payment.Id, payment.Amount, ct);
                payment.Status = PaymentStatus.Succeeded;
                payment.CreditedAt = now;
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
            }

            payment.CompletedAt = now;
        }

        db.PaymentEvents.Add(new PaymentEvent
        {
            Gateway = gateway.Name,
            EventId = verified.EventId,
            PaymentId = payment?.Id,
            Type = verified.Type,
            Amount = verified.Amount,
            Currency = verified.Currency,
            Outcome = outcome,
            Reason = reason,
            PayloadHash = payloadHash,
            ReceivedAt = clock.GetUtcNow()
        });

        audit.Record("payment.event", nameof(PaymentEvent), verified.EventId,
            allowed: outcome is not PaymentEventOutcome.Rejected,
            denyReason: reason,
            organizationId: payment?.OrganizationId,
            metadataJson: $$"""{"type":"{{verified.Type}}","outcome":"{{outcome}}","payment":"{{payment?.Id}}"}""");

        return true;
    }

    private static (PaymentEventOutcome Outcome, string? Reason) Decide(
        IPaymentGateway gateway, VerifiedPaymentEvent verified, Payment? payment)
    {
        if (payment is null)
        {
            return (PaymentEventOutcome.Rejected, "unknown_reference");
        }

        if (!string.Equals(verified.MerchantId, gateway.MerchantId, StringComparison.Ordinal))
        {
            return (PaymentEventOutcome.Rejected, "merchant_mismatch");
        }

        if (!string.Equals(verified.Currency, payment.Currency, StringComparison.Ordinal))
        {
            return (PaymentEventOutcome.Rejected, "currency_mismatch");
        }

        if (verified.Amount != payment.Amount)
        {
            return (PaymentEventOutcome.Rejected, "amount_mismatch");
        }

        return verified.Type switch
        {
            // A success after an earlier failure is a later successful attempt on the same checkout.
            PaymentEventType.PaymentSucceeded => payment.Status == PaymentStatus.Succeeded
                ? (PaymentEventOutcome.Duplicate, "already_credited")
                : (PaymentEventOutcome.Applied, null),

            // A failure arriving after a success is stale; the money was taken.
            PaymentEventType.PaymentFailed => payment.Status switch
            {
                PaymentStatus.Pending => (PaymentEventOutcome.Applied, null),
                PaymentStatus.Failed => (PaymentEventOutcome.Duplicate, "already_failed"),
                _ => (PaymentEventOutcome.Ignored, "stale_failure_after_success")
            },

            // Refund and chargeback handling (negative balances, reversals) needs an approved policy.
            _ => (PaymentEventOutcome.NeedsReview, "refund_policy_not_approved")
        };
    }

    private static PaymentResponse ToResponse(Payment p) =>
        new(p.Id, p.OrganizationId, p.Gateway, p.Amount, p.Currency, p.Status, p.CheckoutUrl, p.CreatedAt, p.CreditedAt);
}
