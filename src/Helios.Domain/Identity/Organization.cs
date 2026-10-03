using Helios.Domain.Common;

namespace Helios.Domain.Identity;

/// <summary>Top of the isolation hierarchy: organization owns workspaces owns projects.</summary>
public class Organization : AuditableEntity, IAggregateRoot
{
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Most the company may commit (reserved plus charged) per calendar month (UTC); null for no cap.</summary>
    public decimal? MonthlySpendLimit { get; set; }

    public List<Workspace> Workspaces { get; set; } = [];
}
