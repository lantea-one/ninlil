using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Presence;

/// <summary>
///     This class is the in-memory buffer behind <see cref="IClientPresenceRecorder" />: every
///     sighting is coalesced into one of three concurrent dictionaries, keeping only the most
///     recent sighting per key, until <see cref="ClientPresenceFlushService" /> drains and persists
///     them. Coalescing is what keeps this cheap enough for the hot query path -- a device sending
///     a thousand queries between flushes costs one dictionary entry, not a thousand writes.
///     Registered as a singleton.
///     <para>
///         Source addresses come from the network and can be spoofed, so the source-address buffer
///         holds at most <see cref="MaxSourceAddressesPerFlush" /> distinct addresses between
///         flushes. Further new addresses are counted and dropped (see
///         <see cref="ClientPresenceSnapshot.DroppedSourceAddresses" />), while an address already
///         in the buffer keeps updating. The identity and hardware buffers are keyed by values
///         that only trusted sources (DHCP, the neighbor table) produce, so they need no cap.
///     </para>
/// </summary>
public sealed class ClientPresenceTracker : IClientPresenceRecorder
{
    /// <summary>
    ///     This field is the most distinct source addresses buffered between two flushes. At
    ///     about 100 bytes each, a full buffer is a few megabytes.
    /// </summary>
    public const int MaxSourceAddressesPerFlush = 65_536;

    /// <summary>
    ///     This field contains pending identity sightings, keyed by protocol version and
    ///     lowercased identifier.
    /// </summary>
    private ConcurrentDictionary<(InternetProtocolVersionEnum, string), Sighting> _identities =
        NewIdentityBuffer();

    /// <summary>
    ///     This field contains pending hardware-address sightings, keyed by lowercase hex MAC and the
    ///     sighting's address family. A dual-stack device is seen over IPv4 and IPv6 under one MAC;
    ///     keeping each family's newest sighting lets an IPv4 identity still learn its IPv4 address
    ///     when an IPv6 sighting of the same MAC is newer.
    /// </summary>
    private ConcurrentDictionary<(string, AddressFamily), Sighting> _hardwareAddresses = new();

    /// <summary>
    ///     This field contains pending source-address sightings awaiting identity resolution.
    /// </summary>
    private ConcurrentDictionary<IPAddress, Sighting> _sourceAddresses = new();

    /// <summary>
    ///     This field counts the distinct entries added to <see cref="_sourceAddresses" />, so
    ///     the cap check does not lock the whole dictionary the way <c>Count</c> does. Under
    ///     concurrent adds the buffer can overshoot the cap by a few entries.
    /// </summary>
    private int _sourceAddressCount;

    /// <summary>
    ///     This field counts new source addresses refused since the last drain because the
    ///     buffer was full.
    /// </summary>
    private long _droppedSourceAddresses;

    /// <inheritdoc />
    public void RecordIdentity(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion,
        IPAddress address,
        DateTimeOffset seenAtUtc
    )
    {
        if (string.IsNullOrWhiteSpace(clientIdentifier))
            return;

        Record(
            _identities,
            (protocolVersion, clientIdentifier.Trim().ToLowerInvariant()),
            new Sighting(Normalize(address), seenAtUtc)
        );
    }

    /// <inheritdoc />
    public void RecordHardwareAddress(
        byte[] macAddress,
        IPAddress address,
        DateTimeOffset seenAtUtc
    )
    {
        if (macAddress is not { Length: 6 })
            return;

        IPAddress normalized = Normalize(address);
        Record(
            _hardwareAddresses,
            (
                Convert.ToHexStringLower(macAddress),
                normalized?.AddressFamily ?? AddressFamily.Unspecified
            ),
            new Sighting(normalized, seenAtUtc)
        );
    }

    /// <inheritdoc />
    public void RecordSourceAddress(IPAddress address, DateTimeOffset seenAtUtc)
    {
        if (address is null)
            return;

        IPAddress normalized = Normalize(address);
        Sighting sighting = new(normalized, seenAtUtc);
        ConcurrentDictionary<IPAddress, Sighting> buffer = _sourceAddresses;

        // A known address only updates its sighting. A new one is stored only below the cap.
        if (buffer.ContainsKey(normalized))
        {
            Record(buffer, normalized, sighting);
            return;
        }

        if (Volatile.Read(ref _sourceAddressCount) >= MaxSourceAddressesPerFlush)
        {
            Interlocked.Increment(ref _droppedSourceAddresses);
            return;
        }

        if (buffer.TryAdd(normalized, sighting))
            Interlocked.Increment(ref _sourceAddressCount);
        else
            Record(buffer, normalized, sighting);
    }

    /// <summary>
    ///     This method atomically swaps in empty buffers and returns the previous ones. A sighting
    ///     recorded in the instant a buffer is being swapped can, rarely, land in the old buffer
    ///     after the flush has already read it and be lost. That is accepted rather than locked
    ///     against, since presence is observed continuously and the device's next sighting
    ///     restores it.
    /// </summary>
    /// <returns>The drained sightings.</returns>
    public ClientPresenceSnapshot Drain()
    {
        ClientPresenceSnapshot snapshot = new(
            Interlocked.Exchange(ref _identities, NewIdentityBuffer()),
            Interlocked.Exchange(ref _hardwareAddresses, new()),
            Interlocked.Exchange(ref _sourceAddresses, new())
        )
        {
            DroppedSourceAddresses = Interlocked.Exchange(ref _droppedSourceAddresses, 0),
        };

        // Reset after the swap; an add racing this reset is counted against the new buffer.
        Interlocked.Exchange(ref _sourceAddressCount, 0);
        return snapshot;
    }

    /// <summary>
    ///     This method keeps whichever of the existing and new sighting is more recent.
    /// </summary>
    private static void Record<TKey>(
        ConcurrentDictionary<TKey, Sighting> buffer,
        TKey key,
        Sighting sighting
    ) =>
        buffer.AddOrUpdate(
            key,
            sighting,
            (_, existing) => sighting.SeenAtUtc >= existing.SeenAtUtc ? sighting : existing
        );

    /// <summary>
    ///     This method puts an address in canonical form (see <see cref="ClientAddress" />), so
    ///     an IPv4 client seen on a dual-stack socket is recorded against its IPv4 identity.
    /// </summary>
    private static IPAddress Normalize(IPAddress address) => ClientAddress.Normalize(address);

    /// <summary>
    ///     This method creates an empty identity buffer.
    /// </summary>
    private static ConcurrentDictionary<
        (InternetProtocolVersionEnum, string),
        Sighting
    > NewIdentityBuffer() => new();
}

/// <summary>
///     This record is one coalesced sighting: when a client was last seen, and at which address.
/// </summary>
/// <param name="Address">The IP address the client was seen at, or <see langword="null" />.</param>
/// <param name="SeenAtUtc">The instant it was seen.</param>
public sealed record Sighting(IPAddress Address, DateTimeOffset SeenAtUtc);

/// <summary>
///     This record holds every sighting drained from <see cref="ClientPresenceTracker" /> in one
///     flush.
/// </summary>
/// <param name="Identities">Sightings of already-known identifiers, keyed by protocol version and lowercase identifier.</param>
/// <param name="HardwareAddresses">Sightings of hardware addresses, keyed by lowercase hex MAC and address family.</param>
/// <param name="SourceAddresses">Sightings of source addresses still awaiting identity resolution.</param>
public sealed record ClientPresenceSnapshot(
    IReadOnlyDictionary<(InternetProtocolVersionEnum, string), Sighting> Identities,
    IReadOnlyDictionary<(string, AddressFamily), Sighting> HardwareAddresses,
    IReadOnlyDictionary<IPAddress, Sighting> SourceAddresses
)
{
    /// <summary>
    ///     This property is how many new source addresses were refused because the buffer held
    ///     <see cref="ClientPresenceTracker.MaxSourceAddressesPerFlush" /> addresses. It is zero
    ///     in normal use; a non-zero value means a flood of spoofed or rotating sources.
    /// </summary>
    public long DroppedSourceAddresses { get; init; }

    /// <summary>
    ///     This property indicates whether the snapshot holds no sightings at all.
    /// </summary>
    public bool IsEmpty =>
        Identities.Count == 0 && HardwareAddresses.Count == 0 && SourceAddresses.Count == 0;
}
