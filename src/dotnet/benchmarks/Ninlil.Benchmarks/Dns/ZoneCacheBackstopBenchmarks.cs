using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures one detection pass of <see cref="ZoneCacheBackstopService" /> that finds
///     nothing to reload, which is what the pass costs almost every time it runs: it reads every
///     enabled zone and the records of every answerable zone, then compares each zone with the
///     cached copy. The data set has 1,000 ordinary zones and one large zone at the
///     <c>Large</c> scale. The caches are loaded outside the measurement, and the setup proves
///     that the pass leaves the cached zones as they are.
/// </summary>
[Config(typeof(StorageConfig))]
public class ZoneCacheBackstopBenchmarks : DnsPathBenchmarkBase
{
    private AuthoritativeZoneCache _zoneCache;
    private ZoneCacheBackstopService _backstop;
    private ErrorCountingLogger _logger;

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        IServiceScopeFactory scopes = Database.Services.GetRequiredService<IServiceScopeFactory>();

        _zoneCache = new AuthoritativeZoneCache(scopes);
        SignedZoneCache signedZoneCache = new(scopes, NullLogger<SignedZoneCache>.Instance);
        await _zoneCache.StartAsync(CancellationToken.None);
        await signedZoneCache.StartAsync(CancellationToken.None);

        _backstop = new ZoneCacheBackstopService(
            _zoneCache,
            signedZoneCache,
            scopes,
            TimeProvider.System,
            _logger = new ErrorCountingLogger()
        );

        CachedZone big = _zoneCache.FindZone(DnsName.Parse(BenchmarkDataSet.BigZoneName));
        Expect(big is not null, "the large zone is cached");
        Expect(
            _zoneCache.GetCachedZones().Count >= Shape.Zones + 1,
            "the cache holds every ordinary zone and the large zone"
        );

        await _backstop.RunPassAsync();
        Expect(
            ReferenceEquals(big, _zoneCache.FindZone(DnsName.Parse(BenchmarkDataSet.BigZoneName))),
            "a pass over unchanged zones reloads none"
        );
        Expect(_logger.ErrorCount == 0, "the pass reads and compares without an error");
    }

    /// <summary>One detection pass over unchanged zones: the reads and the comparison.</summary>
    [Benchmark]
    public async Task<long> DetectionPassWithNoChange()
    {
        await _backstop.RunPassAsync();
        return _backstop.CompletedPassCount;
    }

    /// <summary>
    ///     This class counts the errors the service logs, because a pass swallows a failure and
    ///     a failed pass would otherwise be timed as a fast one.
    /// </summary>
    private sealed class ErrorCountingLogger : ILogger<ZoneCacheBackstopService>
    {
        private int _errorCount;

        /// <summary>This property is the number of errors logged so far.</summary>
        public int ErrorCount => Volatile.Read(ref _errorCount);

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

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
            if (logLevel >= LogLevel.Error)
                Interlocked.Increment(ref _errorCount);
        }
    }
}
