using Helios.Application.Abstractions.Persistence;

namespace Helios.Infrastructure.Persistence.MySql;

public sealed class UnitOfWork(HeliosDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    public async Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken) =>
        await context.Database.BeginTransactionAsync(cancellationToken);
}
