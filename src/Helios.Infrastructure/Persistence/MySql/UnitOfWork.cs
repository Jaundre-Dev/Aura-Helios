using Helios.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Helios.Infrastructure.Persistence.MySql;

public sealed class UnitOfWork(HeliosDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// The retrying execution strategy refuses user-initiated transactions unless the whole unit
    /// runs through it, so the transaction is opened inside the strategy. A retry starts from a
    /// clean change tracker: the failed attempt rolled back, and re-running the delegate must not
    /// stage its rows twice.
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        var attempt = 0;

        try
        {
            return await strategy.ExecuteAsync(async ct =>
            {
                if (attempt++ > 0)
                {
                    context.ChangeTracker.Clear();
                }

                await using var transaction = await context.Database.BeginTransactionAsync(ct);

                var result = await work(ct);
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return result;
            }, cancellationToken);
        }
        catch
        {
            // The transaction rolled back; nothing it staged may survive into a later save in this
            // scope — a stale balance or an orphaned row would otherwise be written by accident.
            context.ChangeTracker.Clear();
            throw;
        }
    }
}
