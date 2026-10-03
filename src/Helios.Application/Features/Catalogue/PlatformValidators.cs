using FluentValidation;
using Helios.Application.Features.ApiKeys;
using Helios.Contracts.ApiKeys;
using Helios.Contracts.Billing;
using Helios.Contracts.Catalogue;

namespace Helios.Application.Features.Catalogue;

public sealed class EnableEntitlementRequestValidator : AbstractValidator<EnableEntitlementRequest>
{
    public EnableEntitlementRequestValidator()
    {
        RuleFor(r => r.ProductSlug).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Environment).IsInEnum();
        RuleFor(r => r.Purpose).MaximumLength(1000);
    }
}

public sealed class CreateApiKeyRequestValidator : AbstractValidator<CreateApiKeyRequest>
{
    public CreateApiKeyRequestValidator()
    {
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Environment).IsInEnum();
        RuleFor(r => r.Scopes).NotNull()
            .Must(s => s is { Count: > 0 and <= ApiKeyService.MaxScopes })
            .WithMessage($"Provide between 1 and {ApiKeyService.MaxScopes} product slugs.");
        RuleFor(r => r.MonthlyBudget).Must(Billing.SpendingLimits.IsValidAmount)
            .WithMessage("Use whole cents between 0 and R10 000 000, or omit for no budget.");
        RuleForEach(r => r.Scopes).NotEmpty().MaximumLength(100).Matches("^[a-z0-9.-]+$")
            .WithMessage("Scopes are product slugs: lowercase letters, digits, dots and hyphens.");
    }
}

public sealed class UpsertBillingProfileRequestValidator : AbstractValidator<UpsertBillingProfileRequest>
{
    public UpsertBillingProfileRequestValidator()
    {
        RuleFor(r => r.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.BillingEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.AddressLine1).NotEmpty().MaximumLength(200);
        RuleFor(r => r.AddressLine2).MaximumLength(200);
        RuleFor(r => r.City).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Province).MaximumLength(100);
        RuleFor(r => r.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(r => r.CountryCode).NotEmpty().Matches("^[A-Za-z]{2}$").WithMessage("Use a two-letter country code.");
        RuleFor(r => r.RegistrationNumber).MaximumLength(50);

        // South African VAT numbers are ten digits beginning with 4. Format only — whether the
        // number is registered is a separate, authorised check (catalogue: company.vat-verify).
        RuleFor(r => r.VatNumber).Matches("^4[0-9]{9}$")
            .When(r => !string.IsNullOrWhiteSpace(r.VatNumber) && string.Equals(r.CountryCode, "ZA", StringComparison.OrdinalIgnoreCase))
            .WithMessage("A South African VAT number is ten digits starting with 4.");
        RuleFor(r => r.VatNumber).MaximumLength(20);
    }
}
