using Helios.Domain.ApiKeys;
using Helios.Domain.Billing;
using Helios.Domain.Catalogue;
using Helios.Domain.Execution;
using Helios.Domain.Identity;
using Helios.Domain.Platform;
using Helios.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace Helios.Application.Abstractions.Persistence;

/// <summary>
/// The data seam. Application handlers query through this rather than through a
/// per-aggregate repository — WP0.2 decided a generic repository over EF Core adds a
/// layer and removes capability.
/// </summary>
/// <remarks>
/// This puts EF Core abstractions in the Application layer, which is deliberate and does
/// not weaken the architecture rule that matters: no <em>model provider</em> SDK reaches
/// Domain or Application. That rule is about vendor lock-in on inference, not on the ORM.
/// </remarks>
public interface IHeliosDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<OrganizationMember> OrganizationMembers { get; }
    DbSet<Workspace> Workspaces { get; }
    DbSet<WorkspaceMember> WorkspaceMembers { get; }
    DbSet<Project> Projects { get; }
    DbSet<ProjectMember> ProjectMembers { get; }
    DbSet<AuditLog> AuditLogs { get; }

    DbSet<ApiProduct> ApiProducts { get; }
    DbSet<ApiProductVersion> ApiProductVersions { get; }
    DbSet<Entitlement> Entitlements { get; }
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<ApiRequest> ApiRequests { get; }
    DbSet<BillingProfile> BillingProfiles { get; }

    DbSet<LedgerAccount> LedgerAccounts { get; }
    DbSet<LedgerTransaction> LedgerTransactions { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<PriceVersion> PriceVersions { get; }
    DbSet<UsageEvent> UsageEvents { get; }
    DbSet<Job> Jobs { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentEvent> PaymentEvents { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
