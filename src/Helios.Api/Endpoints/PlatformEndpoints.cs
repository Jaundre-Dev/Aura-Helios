using System.Text.Json;
using System.Text.RegularExpressions;
using Helios.Api.Middleware;
using Helios.Api.Security;
using Helios.Application.Common;
using Helios.Application.Features.ApiKeys;
using Helios.Application.Features.Billing;
using Helios.Application.Features.Catalogue;
using Helios.Application.Features.Requests;
using Helios.Application.Features.Webhooks;
using Helios.Contracts.ApiKeys;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Requests;
using Helios.Contracts.Webhooks;

namespace Helios.Api.Endpoints;

/// <summary>
/// The customer API surface of plan section 8: catalogue, entitlements, keys, product execution,
/// request records and billing profile.
/// </summary>
public static partial class PlatformEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>Hard ceiling before any product-specific bound; nothing larger is even buffered.</summary>
    public const int AbsoluteMaxBodyBytes = 1024 * 1024;

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        MapCatalogue(app);
        MapEntitlements(app);
        MapApiKeys(app);
        MapRequests(app);
        MapBillingProfile(app);
        MapBilling(app);
        MapWebhooks(app);
        return app;
    }

    private static void MapCatalogue(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogue").WithTags("Catalogue").AllowAnonymous();

        group.MapGet("/", async (CatalogueService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithName("ListCatalogue")
            .WithSummary("Every product with its release state. Only products marked callable can be executed.")
            .Produces<IReadOnlyList<ProductSummaryResponse>>();

        group.MapGet("/{slug}", async (string slug, CatalogueService service, CancellationToken ct) =>
                (await service.GetAsync(slug, ct) is { } product) ? Results.Ok(product) : Results.NotFound())
            .WithName("GetCatalogueProduct")
            .Produces<ProductDetailResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapEntitlements(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/entitlements")
            .WithTags("Entitlements")
            .RequireAuthorization();

        group.MapGet("/", async (Guid organizationId, EntitlementService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(organizationId, ct)))
            .WithName("ListEntitlements")
            .Produces<IReadOnlyList<EntitlementResponse>>();

        group.MapPost("/", async (
                Guid organizationId,
                EnableEntitlementRequest request,
                EntitlementService service,
                CancellationToken ct) =>
                Results.Ok(await service.EnableAsync(organizationId, request, ct)))
            .WithName("EnableEntitlement")
            .WithSummary("Enables a product for one environment. Sandbox is free and self-service.")
            .WithValidation<EnableEntitlementRequest>()
            .Produces<EntitlementResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/{entitlementId:guid}", async (
                Guid organizationId,
                Guid entitlementId,
                EntitlementService service,
                CancellationToken ct) =>
            {
                await service.DisableAsync(organizationId, entitlementId, ct);
                return Results.NoContent();
            })
            .WithName("DisableEntitlement")
            .Produces(StatusCodes.Status204NoContent);
    }

    private static void MapApiKeys(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/api-keys")
            .WithTags("API keys")
            .RequireAuthorization();

        group.MapGet("/", async (ApiKeyService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithName("ListApiKeys")
            .WithSummary("Keys in the caller's current workspace. Secrets are never returned.")
            .Produces<IReadOnlyList<ApiKeyResponse>>();

        group.MapPost("/", async (CreateApiKeyRequest request, ApiKeyService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/v1/api-keys/{created.Key.Id}", created);
            })
            .WithName("CreateApiKey")
            .WithSummary("Creates a scoped key. The secret is in this response only and cannot be retrieved again.")
            .WithValidation<CreateApiKeyRequest>()
            .Produces<CreatedApiKeyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/revoke", async (Guid id, ApiKeyService service, CancellationToken ct) =>
                Results.Ok(await service.RevokeAsync(id, ct)))
            .WithName("RevokeApiKey")
            .WithSummary("Disables the key immediately.")
            .Produces<ApiKeyResponse>();

        group.MapPost("/{id:guid}/rotate", async (Guid id, ApiKeyService service, CancellationToken ct) =>
                Results.Ok(await service.RotateAsync(id, ct)))
            .WithName("RotateApiKey")
            .WithSummary("Issues a replacement with the same settings and revokes the original.")
            .Produces<CreatedApiKeyResponse>();
    }

    private static void MapRequests(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/products/{slug}/requests", async (
                string slug,
                ApiEnvironment? environment,
                HttpContext http,
                ProductRequestService service,
                CancellationToken ct) =>
            {
                var idempotencyKey = ReadIdempotencyKey(http.Request);
                var (body, bytes) = await ReadBoundedJsonAsync(http.Request, ct);

                var result = await service.ExecuteAsync(slug, environment, idempotencyKey, body, bytes, ct);

                if (result.Replayed)
                {
                    http.Response.Headers[ReplayedHeader] = "true";
                }

                // Finished work is 200 with the result; accepted-but-unfinished work is 202 with a
                // status URL. Polling that URL is never billed.
                return result.Accepted
                    ? Results.Accepted($"/api/v1/requests/{result.Envelope.RequestId}", result.Envelope)
                    : Results.Ok(result.Envelope);
            })
            .WithTags("Requests")
            .WithName("ExecuteProduct")
            .WithSummary("Runs a product. Portal users choose ?environment= (default Sandbox); API keys act in their own environment.")
            .RequireAuthorization(HeliosAuthPolicies.ProductCaller)
            .Accepts<JsonElement>("application/json")
            .Produces<ApiRequestEnvelope>()
            .Produces<ApiRequestEnvelope>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        var group = app.MapGroup("/api/v1/requests")
            .WithTags("Requests")
            .RequireAuthorization(HeliosAuthPolicies.ProductCaller);

        group.MapGet("/", async (
                ApiEnvironment? environment,
                string? product,
                int? limit,
                ProductRequestService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListAsync(environment, product, limit ?? 50, ct)))
            .WithName("ListRequests")
            .WithSummary("Request history for the current workspace. Metadata only; polling is never billed.")
            .Produces<IReadOnlyList<ApiRequestSummary>>();

        group.MapGet("/{id:guid}", async (Guid id, ProductRequestService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct) is { } summary) ? Results.Ok(summary) : Results.NotFound())
            .WithName("GetRequest")
            .Produces<ApiRequestSummary>()
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/result", async (Guid id, ProductRequestService service, CancellationToken ct) =>
                (await service.GetResultAsync(id, ct) is { } envelope) ? Results.Ok(envelope) : Results.NotFound())
            .WithName("GetRequestResult")
            .WithSummary("The structured result. Requires the ViewResults permission (not Finance or Developer).")
            .Produces<ApiRequestEnvelope>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status410Gone);

        group.MapPost("/{id:guid}/cancel", async (Guid id, ProductRequestService service, CancellationToken ct) =>
                (await service.CancelAsync(id, ct) is { } summary) ? Results.Ok(summary) : Results.NotFound())
            .WithName("CancelRequest")
            .WithSummary("Cancels a request still waiting in the queue and releases its reservation.")
            .Produces<ApiRequestSummary>()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static void MapBilling(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/billing")
            .WithTags("Billing")
            .RequireAuthorization();

        group.MapGet("/balance", async (Guid organizationId, BillingQueryService service, CancellationToken ct) =>
                Results.Ok(await service.GetBalanceAsync(organizationId, ct)))
            .WithName("GetBalance")
            .WithSummary("Settled, reserved and available Rand balances.")
            .Produces<BalanceResponse>();

        group.MapGet("/transactions", async (Guid organizationId, int? limit, BillingQueryService service, CancellationToken ct) =>
                Results.Ok(await service.GetTransactionsAsync(organizationId, limit ?? 100, ct)))
            .WithName("ListLedgerTransactions")
            .Produces<IReadOnlyList<LedgerTransactionResponse>>();

        group.MapGet("/usage", async (
                Guid organizationId,
                DateTimeOffset? from,
                DateTimeOffset? to,
                BillingQueryService service,
                TimeProvider clock,
                CancellationToken ct) =>
            {
                var end = to ?? clock.GetUtcNow();
                return Results.Ok(await service.GetUsageAsync(organizationId, from ?? end.AddDays(-30), end, ct));
            })
            .WithName("GetUsage")
            .WithSummary("Usage and charges per product for a period (default: last 30 days).")
            .Produces<UsageResponse>();

        group.MapGet("/payments", async (Guid organizationId, PaymentService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(organizationId, ct)))
            .WithName("ListPayments")
            .Produces<IReadOnlyList<PaymentResponse>>();

        group.MapPost("/top-ups", async (
                Guid organizationId,
                CreateTopUpRequest request,
                PaymentService service,
                CancellationToken ct) =>
            {
                var payment = await service.CreateTopUpAsync(organizationId, request, ct);
                return Results.Created($"/api/v1/organizations/{organizationId}/billing/payments", payment);
            })
            .WithName("CreateTopUp")
            .WithSummary("Starts a gateway checkout. Credit is added only when the gateway confirms payment.")
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Gateway-to-server only. Authenticated by the gateway's signature, not by a user or key.
        app.MapPost("/api/v1/payments/callbacks/{gateway}", async (
                string gateway,
                HttpRequest http,
                PaymentService service,
                CancellationToken ct) =>
            {
                if (http.ContentLength > MaxCallbackBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                using var reader = new StreamReader(http.Body);
                var body = await reader.ReadToEndAsync(ct);
                if (body.Length > MaxCallbackBytes)
                {
                    return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                var headers = http.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
                var result = await service.HandleCallbackAsync(gateway, headers, body, ct);

                return result == CallbackResult.Unauthenticated ? Results.Unauthorized() : Results.Ok();
            })
            .WithTags("Billing")
            .WithName("PaymentCallback")
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    private const int MaxCallbackBytes = 64 * 1024;

    private static void MapWebhooks(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/webhooks")
            .WithTags("Webhooks")
            .RequireAuthorization();

        group.MapGet("/", async (WebhookService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithName("ListWebhooks")
            .Produces<IReadOnlyList<WebhookEndpointResponse>>();

        group.MapPost("/", async (CreateWebhookRequest request, WebhookService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/v1/webhooks/{created.Endpoint.Id}", created);
            })
            .WithName("CreateWebhook")
            .WithSummary("Registers an HTTPS endpoint. The signing secret is in this response only.")
            .Produces<CreatedWebhookResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapDelete("/{id:guid}", async (Guid id, WebhookService service, CancellationToken ct) =>
            {
                await service.DeactivateAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeactivateWebhook")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/{id:guid}/deliveries", async (Guid id, WebhookService service, CancellationToken ct) =>
                Results.Ok(await service.ListDeliveriesAsync(id, ct)))
            .WithName("ListWebhookDeliveries")
            .WithSummary("Recent deliveries with status, attempts and last response, including dead letters.")
            .Produces<IReadOnlyList<WebhookDeliveryResponse>>();
    }

    private static void MapBillingProfile(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/billing-profile")
            .WithTags("Billing")
            .RequireAuthorization();

        group.MapGet("/", async (Guid organizationId, BillingProfileService service, CancellationToken ct) =>
                (await service.GetAsync(organizationId, ct) is { } profile) ? Results.Ok(profile) : Results.NoContent())
            .WithName("GetBillingProfile")
            .Produces<BillingProfileResponse>()
            .Produces(StatusCodes.Status204NoContent);

        group.MapPut("/", async (
                Guid organizationId,
                UpsertBillingProfileRequest request,
                BillingProfileService service,
                CancellationToken ct) =>
                Results.Ok(await service.UpsertAsync(organizationId, request, ct)))
            .WithName("UpsertBillingProfile")
            .WithValidation<UpsertBillingProfileRequest>()
            .Produces<BillingProfileResponse>();
    }

    private static string? ReadIdempotencyKey(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(IdempotencyHeader, out var values))
        {
            return null;
        }

        var key = values.Count == 1 ? values[0] : null;

        if (key is null || !SafeIdempotencyKey().IsMatch(key))
        {
            throw new BadHttpRequestException(
                $"{IdempotencyHeader} must be 1–{ProductRequestService.MaxIdempotencyKeyLength} printable ASCII characters.");
        }

        return key;
    }

    /// <summary>
    /// Buffers at most <see cref="AbsoluteMaxBodyBytes"/>, then parses. Returns the byte count so
    /// the product's own, smaller bound can be applied before anything runs.
    /// </summary>
    private static async Task<(JsonElement Body, int Bytes)> ReadBoundedJsonAsync(HttpRequest request, CancellationToken ct)
    {
        if (request.ContentLength > AbsoluteMaxBodyBytes)
        {
            throw new PayloadTooLargeException(AbsoluteMaxBodyBytes);
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;

        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > AbsoluteMaxBodyBytes)
            {
                throw new PayloadTooLargeException(AbsoluteMaxBodyBytes);
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new BadHttpRequestException("A JSON request body is required.");
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            return (document.RootElement.Clone(), (int)buffer.Length);
        }
        catch (JsonException)
        {
            throw new BadHttpRequestException("The request body is not valid JSON.");
        }
    }

    [GeneratedRegex("^[\\x21-\\x7E]{1,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdempotencyKey();
}
