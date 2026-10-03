using Helios.Contracts.Catalogue;
using Helios.Domain.Common;

namespace Helios.Domain.Catalogue;

/// <summary>
/// Whether a company may use a product in one environment. Sandbox and live are separate rows:
/// enabling sandbox never implies live, and enabling anything never starts a paid subscription.
/// </summary>
public class Entitlement : AuditableEntity
{
    public Guid OrganizationId { get; set; }
    public Guid ProductId { get; set; }
    public ApiEnvironment Environment { get; set; }
    public EntitlementState State { get; set; } = EntitlementState.Enabled;

    /// <summary>The customer's stated purpose; required for restricted products before approval.</summary>
    public string? Purpose { get; set; }
}
