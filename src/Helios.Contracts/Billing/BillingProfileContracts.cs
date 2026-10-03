namespace Helios.Contracts.Billing;

/// <summary>
/// The legal and invoicing details of a customer company. Currency is always ZAR in this
/// release: HELIOS bills in Rand.
/// </summary>
public sealed record UpsertBillingProfileRequest(
    string LegalName,
    string BillingEmail,
    string AddressLine1,
    string City,
    string PostalCode,
    string? RegistrationNumber = null,
    string? VatNumber = null,
    string? AddressLine2 = null,
    string? Province = null,
    string CountryCode = "ZA");

public sealed record BillingProfileResponse(
    Guid OrganizationId,
    string LegalName,
    string BillingEmail,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? Province,
    string PostalCode,
    string CountryCode,
    string? RegistrationNumber,
    string? VatNumber,
    string Currency,
    DateTimeOffset UpdatedAt);
