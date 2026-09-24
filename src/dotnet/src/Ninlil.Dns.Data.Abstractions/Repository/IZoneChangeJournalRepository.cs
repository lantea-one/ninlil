using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup an IXFR
///     responder needs: given a client's last-known SOA serial, find every change since then, in
///     order.
/// </summary>
public interface IZoneChangeJournalRepository : IRepository<ZoneChangeJournalEntryEntity>
{
    /// <summary>
    ///     This method lists every change made to a zone since a given serial, in the order they
    ///     happened.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to look up history for.</param>
    /// <param name="sinceSerial">
    ///     The serial to start from; entries whose <see cref="ZoneChangeJournalEntryEntity.OldSerial" />
    ///     is at least this are included.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching entries, oldest first.</returns>
    Task<IReadOnlyList<ZoneChangeJournalEntryEntity>> ListSinceSerialAsync(
        Guid zoneId,
        long sinceSerial,
        CancellationToken stoppingToken = default
    );
}
