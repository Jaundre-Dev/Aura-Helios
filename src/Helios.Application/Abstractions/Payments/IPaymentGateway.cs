using Helios.Contracts.Billing;

namespace Helios.Application.Abstractions.Payments;

/// <summary>
/// A payment gateway adapter for AURA's own service fees. Implementations live in Infrastructure;
/// gateway credentials never leave the server. Only a verified callback can move money in the ledger.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Stable name used in callback routes and stored on payments, e.g. <c>fake-test</c>.</summary>
    string Name { get; }

    /// <summary>The merchant account callbacks must name; events for any other merchant are rejected.</summary>
    string MerchantId { get; }

    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Authenticates a callback (signature, timestamp freshness) and parses it. Returns null for
    /// anything not provably from the gateway; such a callback must have no effect at all.
    /// </summary>
    VerifiedPaymentEvent? Verify(IReadOnlyDictionary<string, string> headers, string rawBody);
}

public sealed record CheckoutRequest(Guid PaymentId, Guid OrganizationId, decimal Amount, string Currency);

public sealed record CheckoutSession(string GatewayReference, string CheckoutUrl);

public sealed record VerifiedPaymentEvent(
    string EventId,
    string GatewayReference,
    PaymentEventType Type,
    decimal Amount,
    string Currency,
    string MerchantId,
    DateTimeOffset OccurredAt);
