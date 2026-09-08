using Helios.Contracts.Agents;
using Helios.Contracts.Common;

namespace Helios.UnitTests;

public class PermissionRiskTests
{
    [Theory]
    [InlineData(ToolPermission.WriteProduction)]
    [InlineData(ToolPermission.DeleteResource)]
    [InlineData(ToolPermission.MergePullRequest)]
    [InlineData(ToolPermission.ExecuteTerminal)]
    [InlineData(ToolPermission.Deploy)]
    [InlineData(ToolPermission.ReadProduction)]
    public void High_impact_permissions_require_approval(ToolPermission permission)
    {
        Assert.True(ToolPermissionRisk.RequiresApproval(permission));
    }

    [Theory]
    [InlineData(ToolPermission.ReadRepository)]
    [InlineData(ToolPermission.WriteRepository)]
    [InlineData(ToolPermission.CreatePullRequest)]
    public void Routine_permissions_do_not_block_on_a_human(ToolPermission permission)
    {
        Assert.False(ToolPermissionRisk.RequiresApproval(permission));
    }

    [Fact]
    public void Every_permission_has_a_declared_risk()
    {
        foreach (var permission in Enum.GetValues<ToolPermission>())
        {
            var risk = ToolPermissionRisk.RiskOf(permission);
            Assert.True(Enum.IsDefined(risk));
        }
    }
}
