using Helios.Domain.Common;

namespace Helios.Domain.Billing;

/// <summary>One per company: who is invoiced, where, and under which tax identifiers.</summary>
public class BillingProfile : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public required string LegalName { get; set; }
    public required string BillingEmail { get; set; }
    public required string AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public required string City { get; set; }
    public string? Province { get; set; }
    public required string PostalCode { get; set; }
    public required string CountryCode { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? VatNumber { get; set; }
    public string Currency { get; set; } = "ZAR";
}
