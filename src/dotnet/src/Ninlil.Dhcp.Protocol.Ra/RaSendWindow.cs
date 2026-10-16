using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class keeps the send windows of <see cref="Icmpv6RaListener" />. Two kinds of window
///     exist and they never share state:
///     <list type="bullet">
///         <item>
///             <description>
///                 <b>Multicast, per interface.</b> RFC 4861 Section 6.2.6 says that
///                 "consecutive Router Advertisements sent to the all-nodes multicast address MUST
///                 be rate limited to no more than one advertisement every MIN_DELAY_BETWEEN_RAS
///                 seconds". A solicited reply that falls inside the window is not dropped: the
///                 RFC has the router "schedule the advertisement to be sent at a time
///                 corresponding to MIN_DELAY_BETWEEN_RAS plus the random value after the previous
///                 advertisement was sent". The periodic loop asks
///                 <see cref="TryReserveMulticastNow" /> and retries when the window ends. A
///                 solicited multicast reply asks <see cref="ReserveNextMulticastSlot" />, which
///                 hands out the next free slot, so the reply is deferred.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <b>Unicast, per destination.</b> The RFC limits only multicast advertisements.
///                 A solicited unicast reply is limited here for a different reason: a spoofed
///                 Router Solicitation flood must not make the router send at line rate.
///                 <see cref="TryReserveUnicast" /> allows one reply per destination per window and
///                 tracks at most <see cref="MaximumUnicastDestinations" /> destinations, so the
///                 table cannot grow with the number of senders. This is not an RFC rule.
///             </description>
///         </item>
///     </list>
///     <see cref="Icmpv6RaListener" /> owns one instance; it calls every method from several
///     tasks, so each method takes <see cref="_lock" />. Time comes from the injected
///     <see cref="TimeProvider" />, so tests move it by hand.
/// </summary>
/// <param name="timeProvider">The clock that decides whether a window has ended.</param>
internal sealed class RaSendWindow(TimeProvider timeProvider)
{
    /// <summary>
    ///     RFC 4861 Section 10's "MIN_DELAY_BETWEEN_RAS": the shortest gap between two multicast
    ///     advertisements on one interface. The unicast window uses the same length.
    /// </summary>
    internal static readonly TimeSpan MinimumDelayBetweenAdvertisements = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     This constant contains the most unicast destinations the window tracks. A destination
    ///     that arrives with the table full of live entries is refused.
    /// </summary>
    internal const int MaximumUnicastDestinations = 256;

    /// <summary>
    ///     This lock guards both tables.
    /// </summary>
    private readonly Lock _lock = new();

    /// <summary>
    ///     This field maps an interface index to the time of its latest multicast slot. The slot
    ///     can lie in the future when a deferred reply has reserved it.
    /// </summary>
    private readonly Dictionary<int, DateTimeOffset> _lastMulticastByInterface = [];

    /// <summary>
    ///     This field maps a unicast destination (scope ID included) to the time of its latest
    ///     reply.
    /// </summary>
    private readonly Dictionary<IPAddress, DateTimeOffset> _lastUnicastByDestination = [];

    /// <summary>
    ///     This property contains the number of unicast destinations currently tracked.
    /// </summary>
    internal int TrackedUnicastDestinations
    {
        get
        {
            lock (_lock)
                return _lastUnicastByDestination.Count;
        }
    }

    /// <summary>
    ///     This method is for the periodic loop. It records a multicast send now on the interface
    ///     when the interface sent none (or reserved no slot) inside the window.
    /// </summary>
    /// <param name="interfaceIndex">The interface the advertisement goes out on.</param>
    /// <param name="retryAfter">
    ///     Zero when the method returns true, otherwise the time left until the window ends.
    /// </param>
    /// <returns>Whether the caller may send now.</returns>
    internal bool TryReserveMulticastNow(int interfaceIndex, out TimeSpan retryAfter)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            if (
                _lastMulticastByInterface.TryGetValue(interfaceIndex, out DateTimeOffset last)
                && now < last + MinimumDelayBetweenAdvertisements
            )
            {
                retryAfter = last + MinimumDelayBetweenAdvertisements - now;
                return false;
            }

            _lastMulticastByInterface[interfaceIndex] = now;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    /// <summary>
    ///     This method is for a solicited multicast reply. It reserves the next free slot on the
    ///     interface, which is now or the end of the window, whichever is later, and records it.
    ///     Several replies in a row therefore get slots one window apart.
    /// </summary>
    /// <param name="interfaceIndex">The interface the advertisement goes out on.</param>
    /// <returns>The time to wait before sending; zero when the slot is now.</returns>
    internal TimeSpan ReserveNextMulticastSlot(int interfaceIndex)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            DateTimeOffset slot = now;

            if (
                _lastMulticastByInterface.TryGetValue(interfaceIndex, out DateTimeOffset last)
                && last + MinimumDelayBetweenAdvertisements > now
            )
                slot = last + MinimumDelayBetweenAdvertisements;

            _lastMulticastByInterface[interfaceIndex] = slot;
            return slot - now;
        }
    }

    /// <summary>
    ///     This method records a unicast reply to the destination when it got none inside the
    ///     window. When the table is full it first drops entries whose window has ended; if it is
    ///     still full the destination is refused, which bounds the table and the reply rate.
    /// </summary>
    /// <param name="destination">The address (with scope ID) the reply goes to.</param>
    /// <returns>Whether the caller may send now.</returns>
    internal bool TryReserveUnicast(IPAddress destination)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            if (_lastUnicastByDestination.TryGetValue(destination, out DateTimeOffset last))
            {
                if (now < last + MinimumDelayBetweenAdvertisements)
                    return false;

                _lastUnicastByDestination[destination] = now;
                return true;
            }

            if (_lastUnicastByDestination.Count >= MaximumUnicastDestinations)
            {
                foreach (
                    IPAddress expired in _lastUnicastByDestination
                        .Where(pair => now >= pair.Value + MinimumDelayBetweenAdvertisements)
                        .Select(pair => pair.Key)
                        .ToList()
                )
                    _lastUnicastByDestination.Remove(expired);

                if (_lastUnicastByDestination.Count >= MaximumUnicastDestinations)
                    return false;
            }

            _lastUnicastByDestination[destination] = now;
            return true;
        }
    }

    /// <summary>
    ///     This method forgets every window. <see cref="Icmpv6RaListener.StartAsync" /> calls it so
    ///     a restart does not inherit the previous run's windows.
    /// </summary>
    internal void Reset()
    {
        lock (_lock)
        {
            _lastMulticastByInterface.Clear();
            _lastUnicastByDestination.Clear();
        }
    }
}
