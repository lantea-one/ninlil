using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class builds a <see cref="TsigKeyLookupCache" /> over a given key repository without a
///     database: a service provider that hands out the repository, and a real
///     <see cref="EntityChangeNotifier" /> a test raises, as a saving repository does, to expire
///     the cache.
/// </summary>
public sealed class TsigKeyCacheHost : IDisposable
{
    private readonly ServiceProvider _provider;

    /// <summary>
    ///     This property contains the notifier the cache subscribed to. A test calls
    ///     <see cref="IEntityChangeNotifier.NotifyChanged" /> with the key entity type after it
    ///     changes the repository's rows.
    /// </summary>
    public EntityChangeNotifier Notifier { get; } = new();

    /// <summary>
    ///     This property contains the cache under test.
    /// </summary>
    public TsigKeyLookupCache Cache { get; }

    /// <summary>
    ///     This constructor builds the cache.
    /// </summary>
    /// <param name="repository">The repository every load reads.</param>
    /// <param name="timeProvider">The clock for the age of the copy.</param>
    /// <param name="logger">The logger load failures go to; none when <see langword="null" />.</param>
    public TsigKeyCacheHost(
        ITsigKeyRepository repository,
        TimeProvider timeProvider,
        ILogger<TsigKeyLookupCache> logger = null
    )
    {
        ServiceCollection services = new();
        services.AddSingleton(repository);
        _provider = services.BuildServiceProvider();

        Cache = new TsigKeyLookupCache(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            Notifier,
            logger ?? NullLogger<TsigKeyLookupCache>.Instance,
            timeProvider
        );
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Cache.Dispose();
        _provider.Dispose();
    }
}
