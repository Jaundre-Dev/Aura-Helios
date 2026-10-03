using FluentValidation;
using Helios.Application.Features.Billing;
using Helios.Contracts.Platform;

namespace Helios.Application.Features.Platform;

public sealed class GrantPlatformRoleRequestValidator : AbstractValidator<GrantPlatformRoleRequest>
{
    public GrantPlatformRoleRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.Role).IsInEnum();
    }
}

public sealed class MfaCodeRequestValidator : AbstractValidator<MfaCodeRequest>
{
    public MfaCodeRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().Matches("^[0-9 ]{6,8}$").WithMessage("Enter the 6-digit code from the authenticator app.");
    }
}

/// <summary>Every platform decision states why; the reason is kept in the audit trail.</summary>
internal static class ReasonRules
{
    public static IRuleBuilderOptions<T, string> IsReason<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MinimumLength(5).MaximumLength(500);
}

public sealed class DecideEntitlementRequestValidator : AbstractValidator<DecideEntitlementRequest>
{
    public DecideEntitlementRequestValidator()
    {
        RuleFor(r => r.Decision).IsInEnum();
        RuleFor(r => r.Reason).IsReason();
    }
}

public sealed class ChangeReleaseStateRequestValidator : AbstractValidator<ChangeReleaseStateRequest>
{
    public ChangeReleaseStateRequestValidator()
    {
        RuleFor(r => r.State).IsInEnum();
        RuleFor(r => r.Reason).IsReason();
    }
}

public sealed class ResolveRequestRequestValidator : AbstractValidator<ResolveRequestRequest>
{
    public ResolveRequestRequestValidator()
    {
        RuleFor(r => r.Action).IsInEnum();
        RuleFor(r => r.Reason).IsReason();
    }
}

public sealed class PublishPriceRequestValidator : AbstractValidator<PublishPriceRequest>
{
    public PublishPriceRequestValidator()
    {
        RuleFor(r => r.ProductSlug).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Environment).IsInEnum();
        RuleFor(r => r.Unit).NotEmpty().MaximumLength(30).Matches("^[a-z_]+$").WithMessage("Units are lowercase words, e.g. 'page' or 'document'.");
        RuleFor(r => r.UnitPrice).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(100_000m);
        RuleFor(r => r.MinimumCharge).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(100_000m);
        RuleFor(r => r.TaxTreatment).NotEmpty().Must(t => PriceService.TaxTreatments.Contains(t))
            .WithMessage($"Use one of: {string.Join(", ", PriceService.TaxTreatments)}.");
    }
}

public sealed class CreditAdjustmentRequestValidator : AbstractValidator<CreditAdjustmentRequest>
{
    public CreditAdjustmentRequestValidator()
    {
        RuleFor(r => r.Amount).NotEqual(0m)
            .Must(a => Math.Abs(a) <= PlatformAdministrationService.MaxAdjustment)
            .WithMessage($"Adjustments are limited to R{PlatformAdministrationService.MaxAdjustment:N0} either way.")
            .Must(a => decimal.Round(a, 2) == a).WithMessage("Adjustments are in whole cents.");
        RuleFor(r => r.Reason).IsReason();
        RuleFor(r => r.Reference).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._:-]+$")
            .WithMessage("References use letters, digits, '.', '_', ':' and '-'.");
    }
}
