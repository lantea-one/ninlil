using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Benchmarks.Storage;

/// <summary>
///     This class measures what the query-log writer does for one batch: a new scope, 500
///     <c>AddAsync</c> calls and one save (<c>DnsQueryLogWriterService.WriteBatchAsync</c>). The
///     rows are dated 2000-01-01 and removed again on cleanup, so the table does not grow from
///     run to run.
/// </summary>
[Config(typeof(StorageConfig))]
public class QueryLogWriteBenchmarks : StorageBenchmarkBase
{
    private const int BatchSize = 500;

    private static readonly DateTimeOffset MarkerTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private List<DnsQueryLogEntryEntity> _template;

    /// <inheritdoc />
    protected override Task OnSetupAsync()
    {
        _template =
        [
            .. BenchmarkDataSet.GenerateQueryLog(
                Shape,
                new Random(BenchmarkDataSet.RandomSeed),
                MarkerTime,
                BatchSize
            ),
        ];
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task OnCleanupAsync() =>
        await InScopeAsync(async provider =>
            await provider
                .GetRequiredService<IDnsQueryLogRepository>()
                .DeleteOlderThanAsync(MarkerTime.AddYears(1))
        );

    /// <summary>Saves one batch of 500 entries.</summary>
    [Benchmark]
    public Task<int> SaveBatchOf500() =>
        InScopeAsync(async provider =>
        {
            IDnsQueryLogRepository repository =
                provider.GetRequiredService<IDnsQueryLogRepository>();

            foreach (DnsQueryLogEntryEntity template in _template)
            {
                // A fresh id and a new instance per save: the entities are tracked by the scope.
                await repository.AddAsync(
                    new DnsQueryLogEntryEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = template.CreatedAtUtc,
                        OccurredAtUtc = MarkerTime,
                        ClientAddress = template.ClientAddress,
                        Protocol = template.Protocol,
                        QueriedName = template.QueriedName,
                        QueriedType = template.QueriedType,
                        ResponseCode = template.ResponseCode,
                        AnswerCount = template.AnswerCount,
                        ClientIdentifier = template.ClientIdentifier,
                        ClientProtocolVersion = template.ClientProtocolVersion,
                    }
                );
            }

            return await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });
}
