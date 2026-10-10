using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This class is the in-memory lookup from a client identifier to the registered client that
///     owns it. The DNS-path engines (the filter engine today) call <see cref="FindAsync" /> after
///     <see cref="IClientIdentityResolver" /> has turned a source address into an identifier, so
///     a query does not join clients and client addresses in the database.
///     <para>
///         The directory holds one <see cref="RefreshingSnapshot{T}" /> built from
///         <see cref="IDnsClientRepository" /> and <see cref="IDnsClientAddressRepository" />.
///         It expires the snapshot when <see cref="IEntityChangeNotifier" /> reports a save of a
///         <see cref="DnsClientEntity" /> or a <see cref="DnsClientAddressEntity" />, so a client
///         added, disabled, moved to another group or given another identifier through the API
///         applies to the next query in this process. A change made by another cluster node or
///         outside the process shows after the time-to-live (<see cref="SnapshotTimeToLive" />).
///         The presence flush saves address rows about every 30 seconds while clients are active;
///         it opens an <see cref="EntityChangeSuppression" /> scope, so those saves do not expire
///         the directory.
///     </para>
///     <para>
///         Identifiers compare case-insensitively: a MAC address or DUID is hexadecimal text, and
///         the relational engines differ in how their collations compare it.
///     </para>
/// </summary>
public sealed class DnsClientDirectory : IDisposable
{
    /// <summary>
    ///     This field is how long a snapshot is used before a background refresh starts. It also
    ///     bounds how long a change made outside this process takes to apply.
    /// </summary>
    public static readonly TimeSpan SnapshotTimeToLive = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This field is the in-memory copy of the clients and their identifiers.
    /// </summary>
    private readonly RefreshingSnapshot<DirectorySnapshot> _snapshot;

    /// <summary>
    ///     This field is the change-notification subscription, removed in <see cref="Dispose" />.
    /// </summary>
    private readonly IDisposable _subscription;

    /// <summary>
    ///     This constructor builds the directory and subscribes it to client changes.
    /// </summary>
    /// <param name="scopeFactory">
    ///     The factory used to reach the client repositories in a short-lived scope, once per load.
    /// </param>
    /// <param name="notifier">The signal that tells this directory when clients were saved.</param>
    /// <param name="logger">The logger load failures are recorded to.</param>
    /// <param name="timeProvider">The clock for the snapshot age; the system clock when <see langword="null" />.</param>
    public DnsClientDirectory(
        IServiceScopeFactory scopeFactory,
        IEntityChangeNotifier notifier,
        ILogger<DnsClientDirectory> logger,
        TimeProvider timeProvider = null
    )
    {
        _snapshot = new RefreshingSnapshot<DirectorySnapshot>(
            token => LoadAsync(scopeFactory, token),
            DirectorySnapshot.Empty,
            SnapshotTimeToLive,
            timeProvider ?? TimeProvider.System,
            logger,
            "client directory"
        );

        // The callback only marks the snapshot out of date; the next read reloads it.
        _subscription = notifier.Subscribe(
            [typeof(DnsClientEntity), typeof(DnsClientAddressEntity)],
            _ => _snapshot.Expire()
        );
    }

    /// <summary>
    ///     This method looks up the registered client that answers to an identifier.
    /// </summary>
    /// <param name="clientIdentifier">A MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
    /// <param name="protocolVersion">Which identifier space <paramref name="clientIdentifier" /> belongs to.</param>
    /// <param name="stoppingToken">The token used to stop waiting for a load.</param>
    /// <returns>
    ///     The owning client, or <see langword="null" /> when no client has this identifier. A
    ///     disabled client is returned with <see cref="DnsClientInfo.IsDisabled" /> set.
    /// </returns>
    public async ValueTask<DnsClientInfo> FindAsync(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        CancellationToken stoppingToken = default
    )
    {
        if (clientIdentifier is null)
            return null;

        DirectorySnapshot snapshot = await _snapshot.GetAsync(stoppingToken);

        return snapshot.Clients.GetValueOrDefault((clientIdentifier, protocolVersion));
    }

    /// <summary>
    ///     This method marks the directory out of date; the next lookup waits for a reload. Tests
    ///     and code that changes clients without a repository save call it.
    /// </summary>
    public void Expire() => _snapshot.Expire();

    /// <summary>
    ///     This method removes the change-notification subscription.
    /// </summary>
    public void Dispose() => _subscription.Dispose();

    /// <summary>
    ///     This method reads every client and client address in a short-lived scope and joins
    ///     them in memory.
    /// </summary>
    private static async Task<DirectorySnapshot> LoadAsync(
        IServiceScopeFactory scopeFactory,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsClientRepository clientRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientRepository>();
        IDnsClientAddressRepository addressRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>();

        IReadOnlyList<DnsClientEntity> clients = await clientRepository.ListAsync(
            null,
            stoppingToken
        );
        IReadOnlyList<DnsClientAddressEntity> addresses = await addressRepository.ListAsync(
            null,
            stoppingToken
        );

        Dictionary<Guid, DnsClientInfo> byId = clients.ToDictionary(
            client => client.Id,
            client => new DnsClientInfo(client.Id, client.ClientGroupId, client.IsDisabled)
        );
        Dictionary<(string, InternetProtocolVersionEnum), DnsClientInfo> byIdentifier = new(
            addresses.Count,
            IdentifierComparer.Instance
        );

        foreach (DnsClientAddressEntity address in addresses)
        {
            if (
                address.ClientIdentifier is not null
                && byId.TryGetValue(address.ClientId, out DnsClientInfo info)
            )
                byIdentifier[(address.ClientIdentifier, address.ProtocolVersion)] = info;
        }

        return new DirectorySnapshot(byIdentifier);
    }

    /// <summary>
    ///     This record is one loaded directory: the clients by identifier.
    /// </summary>
    private sealed record DirectorySnapshot(
        IReadOnlyDictionary<(string, InternetProtocolVersionEnum), DnsClientInfo> Clients
    )
    {
        /// <summary>
        ///     This field is the directory used before the first load succeeds: no clients.
        /// </summary>
        public static readonly DirectorySnapshot Empty = new(
            new Dictionary<(string, InternetProtocolVersionEnum), DnsClientInfo>()
        );
    }

    /// <summary>
    ///     This class compares identifier keys: the identifier text ignores case, the identifier
    ///     space must be equal.
    /// </summary>
    private sealed class IdentifierComparer
        : IEqualityComparer<(string Identifier, InternetProtocolVersionEnum Version)>
    {
        /// <summary>
        ///     This field is the shared instance.
        /// </summary>
        public static readonly IdentifierComparer Instance = new();

        /// <inheritdoc />
        public bool Equals(
            (string Identifier, InternetProtocolVersionEnum Version) x,
            (string Identifier, InternetProtocolVersionEnum Version) y
        ) =>
            x.Version == y.Version
            && string.Equals(x.Identifier, y.Identifier, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc />
        public int GetHashCode((string Identifier, InternetProtocolVersionEnum Version) key) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Identifier),
                key.Version
            );
    }
}

/// <summary>
///     This record is what <see cref="DnsClientDirectory" /> keeps of one registered client: the
///     fields the DNS-path engines decide on.
/// </summary>
/// <param name="ClientId">The <c>Id</c> of the client.</param>
/// <param name="ClientGroupId">The client's group, or <see langword="null" /> when it has none.</param>
/// <param name="IsDisabled">Whether the client is disabled, so no per-client setting applies to it.</param>
public sealed record DnsClientInfo(Guid ClientId, Guid? ClientGroupId, bool IsDisabled);
