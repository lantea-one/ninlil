using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="TsigKeyNameNormalizer" /> against the shared host's SQLite
///     database through the real repository: a mixed-case key is lowercased, an already-lowercase
///     key is left alone, and two keys whose names differ only in case are both kept, unrenamed,
///     with a Warning that names them. The database is shared, so each test looks up only the
///     keys it saved and never asserts an exact count of renamed keys.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TsigKeyNameNormalizerTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task SaveAsync(params string[] names)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ITsigKeyRepository repository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        foreach (string name in names)
            await repository.AddAsync(
                new TsigKeyEntity
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    AlgorithmName = "hmac-sha256",
                    SecretEncrypted = "not-used",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private async Task<(int Renamed, CapturingLogger Logger)> RunAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        CapturingLogger logger = new();
        int renamed = await TsigKeyNameNormalizer.RunAsync(
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
            logger
        );

        return (renamed, logger);
    }

    private async Task<TsigKeyEntity> FindAsync(string name)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
            .FindByNameAsync(name);
    }

    [Fact]
    public async Task RunAsync_WithAMixedCaseKey_LowercasesItAndKeepsTheRest()
    {
        string suffix = Guid.NewGuid().ToString("N");
        await SaveAsync($"Mixed.Key-{suffix}", $"already-lower-{suffix}");

        (int renamed, CapturingLogger logger) = await RunAsync();

        Assert.True(renamed >= 1);
        Assert.Null(await FindAsync($"Mixed.Key-{suffix}"));
        TsigKeyEntity lowered = await FindAsync($"mixed.key-{suffix}");
        Assert.NotNull(lowered);
        Assert.NotNull(lowered.UpdatedAtUtc);
        Assert.Null((await FindAsync($"already-lower-{suffix}")).UpdatedAtUtc);
        Assert.DoesNotContain(logger.Warnings, warning => warning.Contains(suffix));

        // A second run finds nothing of ours to do.
        (_, CapturingLogger second) = await RunAsync();
        Assert.DoesNotContain(second.Warnings, warning => warning.Contains(suffix));
    }

    [Fact]
    public async Task RunAsync_WithKeysThatDifferOnlyInCase_KeepsBothAndWarnsNamingThem()
    {
        string suffix = Guid.NewGuid().ToString("N");
        await SaveAsync($"Dup-{suffix}", $"dup-{suffix}", $"DUP-{suffix}");

        (_, CapturingLogger logger) = await RunAsync();

        Assert.NotNull(await FindAsync($"Dup-{suffix}"));
        Assert.NotNull(await FindAsync($"dup-{suffix}"));
        Assert.NotNull(await FindAsync($"DUP-{suffix}"));
        string warning = Assert.Single(logger.Warnings, text => text.Contains(suffix));
        Assert.Contains($"'Dup-{suffix}'", warning);
        Assert.Contains($"'DUP-{suffix}'", warning);
        Assert.Contains($"'dup-{suffix}'", warning);
    }

    /// <summary>
    ///     This class collects the formatted text of every Warning it is given.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
