using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IUnitOfWork" /> by calling
///     <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)" />.
///     The <see cref="Microsoft.EntityFrameworkCore.DbContext" /> already tracks the changes
///     staged by every repository that shares the same (scoped) context instance.
/// </summary>
/// <param name="context">The database context whose staged changes should be saved.</param>
public class EntityFrameworkUnitOfWork(NinlilDnsContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
        context.SaveChangesAsync(stoppingToken);
}
