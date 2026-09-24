using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IDnsFilterListEntryRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class DnsFilterListEntryRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<DnsFilterListEntryEntity>(context),
        IDnsFilterListEntryRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntryEntity>> ListByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    ) => await Set.Where(entry => entry.ListId == listId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsFilterListEntryEntity>> SearchByListIdAsync(
        Guid listId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(entry => entry.ListId == listId)
            .OrderBy(entry => entry.Value)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    ) => await Set.CountAsync(entry => entry.ListId == listId, stoppingToken);

    /// <inheritdoc />
    public async Task ReplaceEntriesAsync(
        Guid listId,
        IReadOnlyList<DnsFilterListReplacementEntry> entries,
        CancellationToken stoppingToken = default
    )
    {
        // A subscribed list's refresh is a wholesale swap-in, not an entry-by-entry diff: remove
        // every entry the list currently has before adding back the freshly supplied set.
        List<DnsFilterListEntryEntity> existing = await Set.Where(entry => entry.ListId == listId)
            .ToListAsync(stoppingToken);
        Set.RemoveRange(existing);

        // Stamp every freshly added entry with the same creation timestamp, since they are all
        // conceptually part of one refresh operation.
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (DnsFilterListReplacementEntry entry in entries)
            await Set.AddAsync(
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now,
                    ListId = listId,
                    Value = entry.Value,
                    IsException = entry.IsException,
                },
                stoppingToken
            );
    }
}
