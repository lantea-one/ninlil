using Microsoft.EntityFrameworkCore.Storage;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IUnitOfWorkTransactionFactory" /> with a database
///     transaction on the scoped <see cref="NinlilDnsContext" />, the same context every
///     repository and <see cref="EntityFrameworkUnitOfWork" /> share. It works the same on every
///     supported provider (SQLite, PostgreSQL, MySQL and SQL Server) because no provider here
///     enables a retrying execution strategy, which would refuse a user-started transaction.
/// </summary>
/// <param name="context">The database context the transaction is opened on.</param>
public class EntityFrameworkUnitOfWorkTransactionFactory(NinlilDnsContext context)
    : IUnitOfWorkTransactionFactory
{
    /// <inheritdoc />
    public async Task<IUnitOfWorkTransaction> BeginAsync(
        CancellationToken stoppingToken = default
    ) =>
        new EntityFrameworkUnitOfWorkTransaction(
            await context.Database.BeginTransactionAsync(stoppingToken)
        );

    /// <summary>
    ///     This class wraps an EF Core transaction; disposing it before a commit rolls back.
    /// </summary>
    /// <param name="transaction">The underlying EF Core transaction.</param>
    private sealed class EntityFrameworkUnitOfWorkTransaction(IDbContextTransaction transaction)
        : IUnitOfWorkTransaction
    {
        /// <inheritdoc />
        public Task CommitAsync(CancellationToken stoppingToken = default) =>
            transaction.CommitAsync(stoppingToken);

        /// <inheritdoc />
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
