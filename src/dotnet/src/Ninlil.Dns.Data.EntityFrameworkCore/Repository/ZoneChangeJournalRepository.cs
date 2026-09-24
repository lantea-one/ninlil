using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IZoneChangeJournalRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class ZoneChangeJournalRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<ZoneChangeJournalEntryEntity>(context),
        IZoneChangeJournalRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneChangeJournalEntryEntity>> ListSinceSerialAsync(
        Guid zoneId,
        long sinceSerial,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(entry => entry.ZoneId == zoneId && entry.OldSerial >= sinceSerial)
            .OrderBy(entry => entry.OldSerial)
            .ToListAsync(stoppingToken);
}
