namespace Helios.Application.Abstractions.Persistence;

/// <summary>MySQL is the source of truth. Redis never holds durable business state.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken);
}
