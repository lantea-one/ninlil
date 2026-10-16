using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule.Tests;

/// <summary>
///     This class covers how <see cref="DnsClientScheduleCache" /> logs a schedule whose time zone
///     id the host does not know: one warning per id, debug entries on later loads, and a warning of
///     its own for a different bad id. It reuses the in-memory fixture of
///     <see cref="DnsClientScheduleEngineTests" /> and a capturing logger. Reloads are forced with <see cref="DnsClientScheduleCache.Expire" />.
/// </summary>
public sealed class DnsClientScheduleCacheTimeZoneLogTests
{
    /// <summary>
    ///     This class is a logger that keeps every entry's level and formatted message, so a test
    ///     can count warnings. It is enabled for every level.
    /// </summary>
    private sealed class CapturingLogger : ILogger<DnsClientScheduleCache>
    {
        /// <summary>The captured entries, guarded by its own lock (loads run on pool threads).</summary>
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            lock (Entries)
                Entries.Add((logLevel, formatter(state, exception)));
        }

        /// <summary>
        ///     This method counts the entries of any level that mention a time zone id.
        /// </summary>
        public int CountAll(string timeZoneId)
        {
            lock (Entries)
                return Entries.Count(entry => entry.Message.Contains($"'{timeZoneId}'"));
        }

        /// <summary>
        ///     This method counts the entries of a level that mention a time zone id.
        /// </summary>
        public int Count(LogLevel level, string timeZoneId)
        {
            lock (Entries)
                return Entries.Count(entry =>
                    entry.Level == level && entry.Message.Contains($"'{timeZoneId}'")
                );
        }
    }

    /// <summary>
    ///     This method reloads the cache: <see cref="DnsClientScheduleCache.Expire" /> marks the copy
    ///     out of date and the next read awaits one new load. The time-to-live path loads through
    ///     the same method but needs the storage read-routing services a host registers, which the
    ///     in-memory fixture does not have.
    /// </summary>
    private static async Task ReloadAsync(DnsClientScheduleCache cache)
    {
        cache.Expire();
        await cache.GetAsync();
    }

    [Fact]
    public async Task Load_AnUnknownTimeZoneOnThreeLoads_WarnsOnceAndLogsDebugAfterwards()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new();
        fixture.AddClientSchedule(
            Guid.NewGuid(),
            DnsScheduleDayOfWeekEnum.Monday,
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            timeZoneId: "Mars/Olympus_Mons"
        );
        CapturingLogger logger = new();
        using DnsClientScheduleCache cache = new(fixture.ScopeFactory, fixture.Notifier, logger);

        await cache.GetAsync();
        await ReloadAsync(cache);
        await ReloadAsync(cache);

        Assert.Equal(1, logger.Count(LogLevel.Warning, "Mars/Olympus_Mons"));
        Assert.Equal(2, logger.Count(LogLevel.Debug, "Mars/Olympus_Mons"));
    }

    [Fact]
    public async Task Load_ASecondUnknownTimeZone_GetsItsOwnWarning()
    {
        DnsClientScheduleEngineTests.Fixture fixture = new();
        fixture.AddClientSchedule(
            Guid.NewGuid(),
            DnsScheduleDayOfWeekEnum.Monday,
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            timeZoneId: "Mars/Olympus_Mons"
        );
        CapturingLogger logger = new();
        using DnsClientScheduleCache cache = new(fixture.ScopeFactory, fixture.Notifier, logger);
        await cache.GetAsync();

        fixture.AddClientSchedule(
            Guid.NewGuid(),
            DnsScheduleDayOfWeekEnum.Monday,
            new TimeOnly(21, 0),
            new TimeOnly(7, 0),
            timeZoneId: "Venus/Maxwell_Montes"
        );
        await cache.GetAsync();

        Assert.Equal(1, logger.Count(LogLevel.Warning, "Mars/Olympus_Mons"));
        Assert.Equal(1, logger.Count(LogLevel.Warning, "Venus/Maxwell_Montes"));
        Assert.Equal(1, logger.Count(LogLevel.Debug, "Mars/Olympus_Mons"));
    }
}
