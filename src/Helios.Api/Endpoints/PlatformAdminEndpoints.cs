using System.Security.Claims;
using Helios.Api.Middleware;
using Helios.Api.Security;
using Helios.Application.Common;
using Helios.Application.Features.Platform;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;
using Helios.Contracts.Identity;
using Helios.Contracts.Platform;
using Helios.Domain.Execution;
using Helios.Domain.Webhooks;

namespace Helios.Api.Endpoints;

/// <summary>
/// Platform administration for HELIOS staff (plan section 12). A staff member signs in normally,
/// then exchanges an authenticator code for a short platform session; every other route here
/// requires that session and a named platform permission. No route returns customer documents,
/// inputs or results.
/// </summary>
public static class PlatformAdminEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAdminEndpoints(this IEndpointRouteBuilder app)
    {
        MapStepUp(app);

        var group = app.MapGroup("/api/v1/platform")
            .WithTags("Platform administration")
            .RequireAuthorization(HeliosAuthPolicies.PlatformStaff);

        MapStaff(group);
        MapTenants(group);
        MapCatalogueAndMoney(group);
        MapOperations(group);
        return app;
    }

    private static void MapStepUp(IEndpointRouteBuilder app)
    {
        // Ordinary signed-in session: these are how a staff member obtains the platform session.
        var mfa = app.MapGroup("/api/v1/platform/mfa")
            .WithTags("Platform administration")
            .RequireAuthorization()
            .RequireRateLimiting(AuthRateLimiting.PolicyName);

        mfa.MapPost("/enrol", async (PlatformStaffService service, CancellationToken ct) =>
                Results.Ok(await service.EnrolAsync(ct)))
            .WithName("EnrolPlatformMfa")
            .WithSummary("Starts authenticator enrolment. The secret is shown once; confirm it with a code.")
            .Produces<MfaEnrolmentResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        mfa.MapPost("/confirm", (MfaCodeRequest request, ClaimsPrincipal principal, PlatformStaffService service,
                JwtTokenIssuer issuer, CancellationToken ct) => StepUpAsync(request, principal, service, issuer, confirming: true, ct))
            .WithName("ConfirmPlatformMfa")
            .WithValidation<MfaCodeRequest>()
            .Produces<PlatformSessionResponse>();

        mfa.MapPost("/verify", (MfaCodeRequest request, ClaimsPrincipal principal, PlatformStaffService service,
                JwtTokenIssuer issuer, CancellationToken ct) => StepUpAsync(request, principal, service, issuer, confirming: false, ct))
            .WithName("VerifyPlatformMfa")
            .WithSummary("Exchanges a fresh authenticator code for a short platform session.")
            .WithValidation<MfaCodeRequest>()
            .Produces<PlatformSessionResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> StepUpAsync(
        MfaCodeRequest request,
        ClaimsPrincipal principal,
        PlatformStaffService service,
        JwtTokenIssuer issuer,
        bool confirming,
        CancellationToken ct)
    {
        var userId = principal.UserId() ?? throw new UnauthenticatedException();
        var role = await service.VerifyCodeAsync(request.Code, confirming, ct);

        // The stamp was compared with the database by the session validator for this request.
        var stamp = principal.FindFirstValue(HeliosClaims.SecurityStamp) ?? throw new UnauthenticatedException();

        return Results.Ok(issuer.IssuePlatform(userId, principal.Email() ?? string.Empty, principal.DisplayName(), stamp, role));
    }

    private static void MapStaff(RouteGroupBuilder group)
    {
        group.MapPost("/recovery-codes", async (PlatformStaffService service, CancellationToken ct) =>
                Results.Ok(new RecoveryCodesResponse(await service.NewRecoveryCodesAsync(ct))))
            .WithName("NewPlatformRecoveryCodes")
            .WithSummary("Replaces the caller's platform recovery codes (shown once). Each stands in for the authenticator once.")
            .Produces<RecoveryCodesResponse>();

        group.MapGet("/staff", async (PlatformStaffService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(ct)))
            .WithName("ListPlatformStaff")
            .Produces<IReadOnlyList<PlatformStaffResponse>>();

        group.MapPost("/staff", async (GrantPlatformRoleRequest request, PlatformStaffService service, CancellationToken ct) =>
                Results.Ok(await service.GrantAsync(request, ct)))
            .WithName("GrantPlatformRole")
            .WithSummary("Grants or changes a staff role. Never for oneself.")
            .WithValidation<GrantPlatformRoleRequest>()
            .Produces<PlatformStaffResponse>();

        group.MapPost("/staff/{userId:guid}/deactivate", async (Guid userId, PlatformStaffService service, CancellationToken ct) =>
            {
                await service.DeactivateAsync(userId, ct);
                return Results.NoContent();
            })
            .WithName("DeactivatePlatformStaff");

        group.MapPost("/staff/{userId:guid}/reset-mfa", async (Guid userId, PlatformStaffService service, CancellationToken ct) =>
            {
                await service.ResetMfaAsync(userId, ct);
                return Results.NoContent();
            })
            .WithName("ResetPlatformStaffMfa");
    }

    private static void MapTenants(RouteGroupBuilder group)
    {
        group.MapGet("/organizations", async (string? search, int? limit, PlatformAdministrationService service, CancellationToken ct) =>
                Results.Ok(await service.ListOrganizationsAsync(search, limit ?? 50, ct)))
            .WithName("ListPlatformOrganizations")
            .Produces<IReadOnlyList<PlatformOrganizationResponse>>();

        group.MapPost("/organizations/{organizationId:guid}/adjustments", async (
                Guid organizationId,
                CreditAdjustmentRequest request,
                PlatformAdministrationService service,
                CancellationToken ct) =>
                Results.Ok(await service.AdjustCreditAsync(organizationId, request, ct)))
            .WithName("AdjustCredit")
            .WithSummary("Audited manual credit or debit. Repeating a reference posts nothing.")
            .WithValidation<CreditAdjustmentRequest>()
            .Produces<CreditAdjustmentResponse>()
            .ProducesProblem(StatusCodes.Status402PaymentRequired)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/entitlements/pending", async (PlatformAdministrationService service, CancellationToken ct) =>
                Results.Ok(await service.ListPendingEntitlementsAsync(ct)))
            .WithName("ListPendingEntitlements")
            .Produces<IReadOnlyList<PendingEntitlementResponse>>();

        group.MapPost("/entitlements/{entitlementId:guid}/decision", async (
                Guid entitlementId,
                DecideEntitlementRequest request,
                PlatformAdministrationService service,
                CancellationToken ct) =>
                Results.Ok(await service.DecideEntitlementAsync(entitlementId, request, ct)))
            .WithName("DecideEntitlement")
            .WithValidation<DecideEntitlementRequest>()
            .Produces<PendingEntitlementResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/audit", async (
                Guid? organizationId,
                string? action,
                int? limit,
                PlatformAdministrationService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListAuditAsync(organizationId, action, limit ?? 100, ct)))
            .WithName("ListPlatformAudit")
            .Produces<IReadOnlyList<PlatformAuditEntry>>();
    }

    private static void MapCatalogueAndMoney(RouteGroupBuilder group)
    {
        group.MapPost("/products/{slug}/release-state", async (
                string slug,
                ChangeReleaseStateRequest request,
                PlatformAdministrationService service,
                CancellationToken ct) =>
                Results.Ok(await service.ChangeReleaseStateAsync(slug, request, ct)))
            .WithName("ChangeReleaseState")
            .WithSummary("Moves a product between release states; callable states need an implementation, live needs a price.")
            .WithValidation<ChangeReleaseStateRequest>()
            .Produces<ProductSummaryResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/prices", async (string? product, PlatformAdministrationService service, CancellationToken ct) =>
                Results.Ok(await service.ListPricesAsync(product, ct)))
            .WithName("ListPrices")
            .Produces<IReadOnlyList<PriceVersionResponse>>();

        group.MapPost("/prices", async (PublishPriceRequest request, PlatformAdministrationService service, CancellationToken ct) =>
                Results.Ok(await service.PublishPriceAsync(request, ct)))
            .WithName("PublishPrice")
            .WithSummary("Publishes a new immutable price version, taking effect now or later — never in the past.")
            .WithValidation<PublishPriceRequest>()
            .Produces<PriceVersionResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/payment-events", async (
                PaymentEventOutcome? outcome,
                int? limit,
                PlatformAdministrationService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListPaymentEventsAsync(outcome, limit ?? 100, ct)))
            .WithName("ListPaymentEventsForReview")
            .WithSummary("Refunds, chargebacks and mismatched callbacks that changed no money.")
            .Produces<IReadOnlyList<PaymentEventReviewResponse>>();
    }

    private static void MapOperations(RouteGroupBuilder group)
    {
        group.MapGet("/jobs", async (JobStatus? status, int? limit, PlatformOperationsService service, CancellationToken ct) =>
                Results.Ok(await service.ListJobsAsync(status, limit ?? 100, ct)))
            .WithName("ListPlatformJobs")
            .WithSummary("Jobs awaiting review or reconciliation by default. Metadata only.")
            .Produces<IReadOnlyList<PlatformJobResponse>>();

        group.MapPost("/requests/{requestId:guid}/resolve", async (
                Guid requestId,
                ResolveRequestRequest request,
                PlatformOperationsService service,
                CancellationToken ct) =>
                Results.Ok(await service.ResolveAsync(requestId, request, ct)))
            .WithName("ResolveRequest")
            .WithSummary("Releases or re-reconciles a request whose provider outcome is unknown.")
            .WithValidation<ResolveRequestRequest>()
            .Produces<PlatformJobResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/webhook-deliveries", async (
                WebhookDeliveryStatus? status,
                int? limit,
                PlatformOperationsService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListDeliveriesAsync(status, limit ?? 100, ct)))
            .WithName("ListPlatformDeliveries")
            .WithSummary("Dead-lettered deliveries by default.")
            .Produces<IReadOnlyList<PlatformDeliveryResponse>>();

        group.MapPost("/webhook-deliveries/{deliveryId:guid}/retry", async (
                Guid deliveryId,
                PlatformOperationsService service,
                CancellationToken ct) =>
                Results.Ok(await service.RetryDeliveryAsync(deliveryId, ct)))
            .WithName("RetryDelivery")
            .Produces<PlatformDeliveryResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
