namespace Helios.Contracts.Organizations;

/// <summary>
/// A person's role in a customer company. These are named permission sets, not an ordinal
/// ladder: Finance is not "above" Operator, it simply sees different things. Authorisation
/// asks for a <see cref="OrganizationPermission"/>, never compares roles numerically.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OrganizationRole>))]
public enum OrganizationRole
{
    Owner,
    Admin,
    Developer,
    Finance,
    Operator,
    Auditor
}

/// <summary>Individual capabilities a role grants. Checked by name at every protected action.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<OrganizationPermission>))]
public enum OrganizationPermission
{
    /// <summary>Read the company profile and its own membership.</summary>
    ViewOrganization,

    /// <summary>Change company details and policies.</summary>
    ManageOrganization,

    /// <summary>Invite, change and remove team members.</summary>
    ManageTeam,

    /// <summary>Create workspaces under the company.</summary>
    ManageWorkspaces,

    /// <summary>Create, rotate and revoke API keys.</summary>
    ManageApiKeys,

    /// <summary>Enable products and request production access.</summary>
    ManageEntitlements,

    /// <summary>Run entitled products through the portal.</summary>
    ExecuteProducts,

    /// <summary>Read structured results of document and verification requests.</summary>
    ViewResults,

    /// <summary>Read redacted request metadata and diagnostics (no result payloads).</summary>
    ViewRequestDiagnostics,

    /// <summary>Read balances, usage, invoices and payments.</summary>
    ViewBilling,

    /// <summary>Change billing details and start top-ups.</summary>
    ManageBilling,

    /// <summary>Read the company audit trail.</summary>
    ViewAudit,

    /// <summary>Approve, correct or reject document results. Originals are always kept.</summary>
    ReviewResults,

    /// <summary>Accept customer terms and the processing agreement for the company. Owner only.</summary>
    AcceptAgreements
}
