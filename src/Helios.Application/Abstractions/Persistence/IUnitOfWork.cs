namespace Helios.Application.Abstractions.Persistence;

/// <summary>MySQL is the source of truth. Redis never holds durable business state.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction and commits it when the delegate
    /// returns. Any exception rolls the whole unit back. Runs under the configured execution
    /// strategy, so a transient failure may run the delegate again from the start: stage every
    /// change inside it and keep side effects outside the database out of it.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}
