using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class is the base of the benchmarks that measure what a DNS engine reads from the
///     database for one query: the filter engine, the rewrite engine, the client schedule
///     engine and the forwarder lookup. It builds the same service provider as the storage
///     benchmarks, adds the fixed client-identity resolver (see
///     <see cref="BenchmarkClientIdentityResolver" />) and lets each subclass register the
///     engine it measures, the way the host's <c>AddNinlilDnsServer*</c> extensions do. No
///     sockets are involved.
///     <para>
///         Each subclass checks the result of every benchmark method once in
///         <c>OnSetupAsync</c> through <see cref="Expect" />, so a broken setup (an empty
///         database, an engine switched off) fails the run instead of producing fast nonsense.
///     </para>
/// </summary>
public abstract class DnsPathBenchmarkBase : StorageBenchmarkBase
{
    /// <inheritdoc />
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    ) => services.AddSingleton<IClientIdentityResolver, BenchmarkClientIdentityResolver>();

    /// <summary>
    ///     This method throws when <paramref name="condition" /> is false, naming the check.
    /// </summary>
    protected static void Expect(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(
                $"Benchmark setup check failed: {description}. The seeded data set or the engine wiring does not match what this benchmark expects."
            );
    }
}
