using Helios.Contracts.Organizations;

namespace Helios.Domain.Identity;

/// <summary>
/// The role → permission map from HELIOS-IMPLEMENTATION-PLAN.md section 5. Finance can see
/// money but not documents; Developers manage keys and see redacted diagnostics but not
/// result payloads; Operators run products and read their results; Auditors read the trail.
/// </summary>
public static class OrganizationPermissions
{
    private static readonly IReadOnlyDictionary<OrganizationRole, HashSet<OrganizationPermission>> Map =
        new Dictionary<OrganizationRole, HashSet<OrganizationPermission>>
        {
            [OrganizationRole.Owner] = [.. Enum.GetValues<OrganizationPermission>()],

            [OrganizationRole.Admin] =
            [
                OrganizationPermission.ViewOrganization,
                OrganizationPermission.ManageOrganization,
                OrganizationPermission.ManageTeam,
                OrganizationPermission.ManageWorkspaces,
                OrganizationPermission.ManageApiKeys,
                OrganizationPermission.ManageEntitlements,
                OrganizationPermission.ExecuteProducts,
                OrganizationPermission.ViewResults,
                OrganizationPermission.ReviewResults,
                OrganizationPermission.ViewRequestDiagnostics,
                OrganizationPermission.ViewBilling,
                OrganizationPermission.ViewAudit,
            ],

            [OrganizationRole.Developer] =
            [
                OrganizationPermission.ViewOrganization,
                OrganizationPermission.ManageApiKeys,
                OrganizationPermission.ExecuteProducts,
                OrganizationPermission.ViewRequestDiagnostics,
            ],

            [OrganizationRole.Finance] =
            [
                OrganizationPermission.ViewOrganization,
                OrganizationPermission.ViewBilling,
                OrganizationPermission.ManageBilling,
            ],

            [OrganizationRole.Operator] =
            [
                OrganizationPermission.ViewOrganization,
                OrganizationPermission.ExecuteProducts,
                OrganizationPermission.ViewResults,
                OrganizationPermission.ReviewResults,
            ],

            [OrganizationRole.Auditor] =
            [
                OrganizationPermission.ViewOrganization,
                OrganizationPermission.ViewAudit,
                OrganizationPermission.ViewRequestDiagnostics,
            ],
        };

    public static bool Grants(OrganizationRole role, OrganizationPermission permission) =>
        Map.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    public static IReadOnlyCollection<OrganizationPermission> For(OrganizationRole role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : [];
}
