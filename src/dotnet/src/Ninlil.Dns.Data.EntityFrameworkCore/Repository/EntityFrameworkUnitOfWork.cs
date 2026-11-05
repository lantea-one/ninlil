using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IUnitOfWork" /> by calling
///     <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)" />.
///     The <see cref="Microsoft.EntityFrameworkCore.DbContext" /> already tracks the changes
///     staged by every repository that shares the same (scoped) context instance. A save that a
///     unique index or primary key rejects throws <see cref="ConflictException" />, the same
///     exception the MongoDB unit of work throws; see <see cref="UniqueConstraintViolation" />.
/// </summary>
/// <param name="context">The database context whose staged changes should be saved.</param>
/// <param name="logger">
///     The logger that records the driver error behind a unique-index conflict. Null outside a
///     dependency-injection scope that registers it.
/// </param>
public class EntityFrameworkUnitOfWork(
    NinlilDnsContext context,
    ILogger<EntityFrameworkUnitOfWork> logger = null
) : IUnitOfWork
{
    /// <inheritdoc />
    /// <exception cref="ConflictException">
    ///     A write violated a unique index (for example a second DHCP custom option with the same
    ///     tag on one scope). The API maps it to 409.
    /// </exception>
    public async Task<int> SaveChangesAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(stoppingToken);
        }
        catch (DbUpdateException exception)
            when (UniqueConstraintViolation.IsUniqueViolation(exception, out string detail))
        {
            // The controllers answer 409 for a duplicate they see first. Two concurrent writes can
            // both pass that check, and the unique index rejects the second one here. The 409
            // text stays generic; the logged detail names the index or the columns.
            logger?.LogWarning(
                exception,
                "A write was rejected by a unique index: {Detail}",
                detail
            );
            throw new ConflictException(
                "A value that must be unique is already in use by another entry."
            );
        }
    }
}
