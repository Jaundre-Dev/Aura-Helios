using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Helios.Application.Abstractions.Payments;
using Helios.Contracts.Billing;

namespace Helios.Infrastructure.Payments;

public sealed class FakeTestGatewayOptions
{
    public const string Name = "fake-test";

    /// <summary>Shared secret for callback signatures. Test value only; never a real credential.</summary>
    public required string WebhookSecret { get; init; }

    public string MerchantId { get; init; } = "fake-merchant";

    /// <summary>A deliberately unroutable base, so a fake checkout link can never take real money.</summary>
    public string CheckoutBaseUrl { get; init; } = "https://payments.invalid/fake-checkout/";

    public TimeSpan Tolerance { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// A stand-in payment gateway for development, sandbox and tests, used because no gateway contract
/// exists yet. It implements the same security a real adapter must: HMAC-SHA256 signatures over a
/// timestamped body, constant-time comparison and a freshness window against replay. It moves no
/// money; Production refuses to start with it configured (<c>ProductionSafetyCheck</c>).
/// <para>
/// Callback: header <c>X-Fake-Signature: t={unix seconds},v1={hex HMAC of "{t}.{body}"}</c>; body
/// <c>{"id","type","reference","amount","currency","merchant","occurredAt"}</c>.
/// </para>
/// </summary>
public sealed class FakeTestPaymentGateway(FakeTestGatewayOptions options, TimeProvider clock) : IPaymentGateway
{
    public const string SignatureHeader = "X-Fake-Signature";

    public string Name => FakeTestGatewayOptions.Name;
    public string MerchantId => options.MerchantId;

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken)
    {
        var reference = $"fake_{request.PaymentId:N}";
        return Task.FromResult(new CheckoutSession(reference, options.CheckoutBaseUrl + reference));
    }

    public VerifiedPaymentEvent? Verify(IReadOnlyDictionary<string, string> headers, string rawBody)
    {
        if (!headers.TryGetValue(SignatureHeader, out var header) || !TryParseHeader(header, out var timestamp, out var signature))
        {
            return null;
        }

        var age = clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (age.Duration() > options.Tolerance)
        {
            return null;
        }

        var expected = Sign(options.WebhookSecret, rawBody, timestamp);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature)))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;

            var type = root.GetProperty("type").GetString() switch
            {
                "payment.succeeded" => PaymentEventType.PaymentSucceeded,
                "payment.failed" => PaymentEventType.PaymentFailed,
                "payment.refunded" => PaymentEventType.Refunded,
                "payment.chargeback" => PaymentEventType.Chargeback,
                _ => (PaymentEventType?)null
            };

            if (type is null)
            {
                return null;
            }

            return new VerifiedPaymentEvent(
                root.GetProperty("id").GetString()!,
                root.GetProperty("reference").GetString()!,
                type.Value,
                decimal.Parse(root.GetProperty("amount").GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture),
                root.GetProperty("currency").GetString()!,
                root.GetProperty("merchant").GetString()!,
                root.GetProperty("occurredAt").GetDateTimeOffset());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The <c>v1</c> signature for a body at a timestamp. Public so tests and tooling can sign callbacks.</summary>
    public static string Sign(string secret, string body, long timestamp) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{timestamp}.{body}"))));

    private static bool TryParseHeader(string header, out long timestamp, out string signature)
    {
        timestamp = 0;
        signature = string.Empty;

        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                continue;
            }

            if (pair[0] == "t")
            {
                long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out timestamp);
            }
            else if (pair[0] == "v1")
            {
                signature = pair[1];
            }
        }

        return timestamp > 0 && signature.Length == 64;
    }
}
