using Helios.Application.Abstractions.Webhooks;
using Helios.Domain.Webhooks;
using Helios.Infrastructure.Persistence.MySql;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Webhooks;

/// <summary>Claims due deliveries with <c>FOR UPDATE SKIP LOCKED</c>, leasing each to one sender.</summary>
public sealed class MySqlWebhookQueue(HeliosDbContext db, TimeProvider clock) : IWebhookQueue
{
    public async Task<ClaimedDelivery?> ClaimNextAsync(TimeSpan lease, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var now = clock.GetUtcNow();
            var pending = nameof(WebhookDeliveryStatus.Pending);

            var rows = await db.WebhookDeliveries
                .FromSqlInterpolated($"""
                    SELECT * FROM webhook_deliveries
                    WHERE status = {pending}
                      AND next_attempt_at <= {now}
                      AND (lease_expires_at IS NULL OR lease_expires_at < {now})
                    ORDER BY next_attempt_at
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .IgnoreQueryFilters()
                .ToListAsync(ct);

            if (rows.SingleOrDefault() is not { } delivery)
            {
                await transaction.RollbackAsync(ct);
                return null;
            }

            delivery.Attempts++;
            delivery.LeaseExpiresAt = now + lease;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new ClaimedDelivery(delivery.Id, delivery.WorkspaceId);
        }, cancellationToken);
    }
}
