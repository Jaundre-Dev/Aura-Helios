using Helios.Contracts.Organizations;
using Helios.Domain.Identity;

namespace Helios.UnitTests;

/// <summary>
/// The role map is a security boundary, so its separations are pinned here: finance never reads
/// documents, developers never read result payloads, and only the Owner holds every permission.
/// </summary>
public class OrganizationPermissionsTests
{
    [Fact]
    public void Owner_holds_every_permission()
    {
        Assert.All(Enum.GetValues<OrganizationPermission>(),
            permission => Assert.True(OrganizationPermissions.Grants(OrganizationRole.Owner, permission)));
    }

    [Fact]
    public void Finance_sees_billing_but_never_results_or_keys()
    {
        Assert.True(OrganizationPermissions.Grants(OrganizationRole.Finance, OrganizationPermission.ViewBilling));
        Assert.True(OrganizationPermissions.Grants(OrganizationRole.Finance, OrganizationPermission.ManageBilling));
        Assert.False(OrganizationPermissions.Grants(OrganizationRole.Finance, OrganizationPermission.ViewResults));
        Assert.False(OrganizationPermissions.Grants(OrganizationRole.Finance, OrganizationPermission.ExecuteProducts));
        Assert.False(OrganizationPermissions.Grants(OrganizationRole.Finance, OrganizationPermission.ManageApiKeys));
    }

    [Fact]
    public void Developer_manages_keys_and_diagnostics_but_not_results_billing_or_team()
    {
        Assert.True(OrganizationPermissions.Grants(OrganizationRole.Developer, OrganizationPermission.ManageApiKeys));
        Assert.True(OrganizationPermissions.Grants(OrganizationRole.Developer, OrganizationPermission.ViewRequestDiagnostics));
        Assert.False(OrganizationPermissions.Grants(OrganizationRole.Developer, OrganizationPermission.ViewResults));
        Assert.False(OrganizationPermissions.Grants(OrganizationRole.Developer, OrganizationPermission.ViewBilling));
        Assert.False(OrganizationPermissions.Grants(OrganizationRole.Developer, OrganizationPermission.ManageTeam));
    }

    [Fact]
    public void Admin_cannot_manage_billing()
    {
        Assert.False(OrganizationPermissions.Grants(OrganizationRole.Admin, OrganizationPermission.ManageBilling));
        Assert.True(OrganizationPermissions.Grants(OrganizationRole.Admin, OrganizationPermission.ManageTeam));
    }

    [Theory]
    [InlineData(OrganizationRole.Operator)]
    [InlineData(OrganizationRole.Auditor)]
    [InlineData(OrganizationRole.Finance)]
    [InlineData(OrganizationRole.Developer)]
    public void Only_owner_and_admin_create_workspaces_or_manage_the_team(OrganizationRole role)
    {
        Assert.False(OrganizationPermissions.Grants(role, OrganizationPermission.ManageWorkspaces));
        Assert.False(OrganizationPermissions.Grants(role, OrganizationPermission.ManageTeam));
    }

    [Fact]
    public void Every_role_can_view_its_own_organization()
    {
        Assert.All(Enum.GetValues<OrganizationRole>(),
            role => Assert.True(OrganizationPermissions.Grants(role, OrganizationPermission.ViewOrganization)));
    }
}
