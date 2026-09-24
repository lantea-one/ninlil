using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IUnitOfWork" /> by simply delegating to
///     <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)" />,
///     since Entity Framework Core's <see cref="Microsoft.EntityFrameworkCore.DbContext" />
///     already tracks every change staged across every repository built on top of the same
///     context instance.
/// </summary>
/// <param name="context">The database context whose staged changes should be saved.</param>
public class EntityFrameworkUnitOfWork(NinlilDnsContext context) : IUnitOfWork
{
    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default) =>
        context.SaveChangesAsync(stoppingToken);
}
