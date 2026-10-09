using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures the retention-by-count delete (<c>DeleteExcessAsync</c>, run by the
///     cleanup service every interval) on the full table with a limit of nine tenths of its
///     rows. Each iteration deletes a tenth of the table, so <c>[IterationSetup]</c> inserts it
///     again outside the measurement. It runs three monitored iterations of one invocation each,
///     because one delete changes what the next one finds. On MongoDB at the large scale the
///     delete fails today (the <c>$nin</c> list passes the command size limit); BenchmarkDotNet
///     reports that as the result.
/// </summary>
[Config(typeof(MonitoredConfig))]
public class QueryLogRetentionBenchmarks : StorageBenchmarkBase
{
    private int _retained;

    /// <inheritdoc />
    protected override Task OnSetupAsync()
    {
        _retained = Shape.QueryLogEntries / 10 * 9;
        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method tops the table up to its seeded size, so every iteration deletes the same
    ///     number of rows.
    /// </summary>
    [IterationSetup]
    public void TopUp()
    {
        int toAdd = InScopeAsync(async provider =>
                Shape.QueryLogEntries
                - await provider
                    .GetRequiredService<IDnsQueryLogRepository>()
                    .CountAsync(new DnsQueryLogSearchCriteria())
            )
            .GetAwaiter()
            .GetResult();

        if (toAdd <= 0)
            return;

        SeedWriter writer = new(Database.Services, TextWriter.Null);
        writer
            .AddAllAsync<DnsQueryLogEntryEntity, IDnsQueryLogRepository>(
                "refill",
                BenchmarkDataSet.GenerateQueryLog(
                    Shape,
                    new Random(Environment.TickCount),
                    DateTimeOffset.UtcNow,
                    toAdd
                )
            )
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Deletes the oldest tenth of the table.</summary>
    [Benchmark]
    public Task<int> DeleteExcess() =>
        InScopeAsync(provider =>
            provider.GetRequiredService<IDnsQueryLogRepository>().DeleteExcessAsync(_retained)
        );
}
