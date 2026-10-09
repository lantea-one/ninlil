using System.Net;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class stands in for the host's client-identity resolver in the DNS-path benchmarks.
///     The real resolver combines static mappings and the DHCP lease snapshot; both are
///     snapshots that cost nothing per query, so the benchmarks use a fixed map from three
///     source addresses to client identifiers of the seeded data set and measure only what the
///     engines read from the database after that. Any other address is unidentified.
/// </summary>
public sealed class BenchmarkClientIdentityResolver : IClientIdentityResolver
{
    /// <summary>
    ///     This constant contains an address no client has: it matches no group network and no
    ///     client, so the default group applies.
    /// </summary>
    public static readonly IPAddress Unidentified = IPAddress.Parse("172.16.9.9");

    /// <summary>
    ///     This constant contains the address of client 2: it lies in the network of group 1
    ///     (<c>10.1.0.0/16</c>) and the client belongs to group 2, so two groups apply.
    /// </summary>
    public static readonly IPAddress InTwoGroups = IPAddress.Parse(
        BenchmarkDataSet.ClientAddress(2)
    );

    /// <summary>
    ///     This constant contains the address of client 0, which has a schedule (22:00 to 23:00
    ///     UTC every day).
    /// </summary>
    public static readonly IPAddress Scheduled = IPAddress.Parse(BenchmarkDataSet.ClientAddress(0));

    /// <summary>
    ///     This constant contains the address of client 25, which has no schedule at either
    ///     scale (the schedules belong to the first 5 clients at the small scale and the first
    ///     20 at the large scale).
    /// </summary>
    public static readonly IPAddress Unscheduled = IPAddress.Parse(
        BenchmarkDataSet.ClientAddress(25)
    );

    private static readonly Dictionary<IPAddress, ClientIdentityResolution> Map = new()
    {
        [InTwoGroups] = Resolution(2),
        [Scheduled] = Resolution(0),
        [Unscheduled] = Resolution(25),
    };

    /// <inheritdoc />
    public Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Map.GetValueOrDefault(remoteAddress));

    private static ClientIdentityResolution Resolution(int rank) =>
        new(BenchmarkDataSet.ClientIdentifier(rank), InternetProtocolVersionEnum.IPv4);
}
