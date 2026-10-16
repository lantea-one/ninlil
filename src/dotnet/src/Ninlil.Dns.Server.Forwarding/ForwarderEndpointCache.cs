using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Forwarding;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class holds the parsed form of every forwarder definition in use and, for host-name
///     definitions, the address the name resolves to. <see cref="ForwarderQueryResolver" /> asks it
///     for a sendable definition per query and <see cref="ForwarderEndpointWarmUpService" />
///     fills it at startup.
///     <para>
///         <b>Lookups run in the background.</b> A query never waits for DNS: the first use of a
///         host name starts one lookup through <see cref="IForwarderHostResolver" /> and returns
///         <see langword="null" />, so the resolver skips that forwarder for this query. At most
///         one lookup per definition runs at a time.
///     </para>
///     <para>
///         <b>Refresh.</b> A resolved address is served while a lookup older than
///         <see cref="RefreshInterval" /> runs on the next use (stale while revalidate), so an
///         address change reaches the forwarder within about five minutes without a restart. A
///         failed lookup keeps the last good address. One Warning is logged when a streak reaches
///         two failures and one Information when the name resolves again; single failures are
///         Debug. With no address ever known, the definition stays unsendable and the next use
///         after <see cref="RefreshInterval" /> tries again.
///     </para>
///     <para>
///         Literal addresses, DoH and DNSCrypt definitions are sendable at once and never looked up.
///         An unparseable definition is logged once and stays unsendable.
///     </para>
/// </summary>
/// <param name="parser">The definition parser; it never queries DNS.</param>
/// <param name="hostResolver">The resolver for host names.</param>
/// <param name="logger">The logger that lookup failures and recoveries are recorded to.</param>
/// <param name="timeProvider">The clock for the refresh age; the system clock when <see langword="null" />.</param>
public sealed class ForwarderEndpointCache(
    IUpstreamForwarderDefinitionParser parser,
    IForwarderHostResolver hostResolver,
    ILogger<ForwarderEndpointCache> logger,
    TimeProvider timeProvider = null
)
{
    /// <summary>
    ///     This field is how long a lookup result is used before the next use starts a new one.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This field bounds one lookup, so a resolver that never answers cannot keep a
    ///     definition's lookup slot for good.
    /// </summary>
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This field is how many failures in a row raise the Warning.
    /// </summary>
    private const int WarnAfterFailures = 2;

    /// <summary>
    ///     This field is the clock for the refresh age.
    /// </summary>
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This field holds the state of every definition string seen, by definition string.
    /// </summary>
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    /// <summary>
    ///     This method returns the definition ready to send to.
    /// </summary>
    /// <param name="definitionString">The forwarder definition string.</param>
    /// <returns>
    ///     The definition with its address filled in, or <see langword="null" /> when the string
    ///     does not parse or its host name has no known address yet (a lookup is then running or
    ///     was just started, and is not awaited).
    /// </returns>
    public UpstreamForwarderDefinition TryGetSendable(string definitionString)
    {
        if (string.IsNullOrWhiteSpace(definitionString))
            return null;

        Entry entry = _entries.TryGetValue(definitionString, out Entry existing)
            ? existing
            : CreateEntry(definitionString);

        if (entry.Parsed is null)
            return null;

        if (!entry.NeedsLookup)
            return entry.Parsed;

        bool startLookup = false;

        lock (entry)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();

            // One lookup at a time, and a new one only after RefreshInterval since the last
            // started, so a name that fails is not hammered on every query.
            if (
                !entry.IsRunning
                && (entry.LastStartedAt is null || now - entry.LastStartedAt >= RefreshInterval)
            )
            {
                entry.IsRunning = true;
                entry.LastStartedAt = now;
                startLookup = true;
            }
        }

        if (startLookup)
            entry.Lookup = LookupAsync(entry);

        return entry.Sendable;
    }

    /// <summary>
    ///     This method forgets the definitions that are no longer configured, so removed
    ///     forwarders do not stay in memory and a definition that comes back is looked up afresh.
    /// </summary>
    /// <param name="definitionStrings">Every definition string still configured.</param>
    public void Retain(IReadOnlySet<string> definitionStrings)
    {
        foreach (string key in _entries.Keys)
        {
            if (!definitionStrings.Contains(key))
                _entries.TryRemove(key, out _);
        }
    }

    /// <summary>
    ///     This method completes when no lookup is running. Tests use it to wait for a lookup
    ///     they let finish.
    /// </summary>
    internal Task WhenIdleAsync() =>
        Task.WhenAll(
            _entries
                .Values.Select(entry => entry.Lookup)
                .Where(lookup => lookup is not null)
                .ToArray()
        );

    /// <summary>
    ///     This method parses a definition string for the first time and stores the result. The
    ///     parse failure is logged only by the call that stored the entry, so an unparseable
    ///     definition produces one Warning, not one per query.
    /// </summary>
    private Entry CreateEntry(string definitionString)
    {
        UpstreamForwarderDefinition parsed = null;
        string parseError = null;

        try
        {
            parsed = parser.Parse(definitionString);
        }
        catch (FormatException exception)
        {
            parseError = exception.Message;
        }

        Entry created = new(parsed);

        if (!_entries.TryAdd(definitionString, created))
            return _entries[definitionString];

        if (parseError is not null)
            logger.LogWarning(
                "Skipping an unparseable forwarder definition '{Definition}': {Error}",
                definitionString,
                parseError
            );

        return created;
    }

    /// <summary>
    ///     This method runs one lookup and records its outcome on the entry. It never throws.
    /// </summary>
    private async Task LookupAsync(Entry entry)
    {
        string host = entry.Parsed.TlsHostName;

        try
        {
            using CancellationTokenSource timeout = new(LookupTimeout);
            IPAddress[] addresses = await hostResolver.ResolveAsync(host, timeout.Token);

            // IPv4 first: a host that publishes both is reachable over IPv4 on every network a
            // forwarder is likely to sit on, while an IPv6 address may have no route.
            IPAddress chosen =
                addresses?.FirstOrDefault(address =>
                    address.AddressFamily == AddressFamily.InterNetwork
                ) ?? addresses?.FirstOrDefault();

            if (chosen is null)
                throw new InvalidOperationException("The host resolved to no addresses.");

            RecordSuccess(entry, host, chosen);
        }
        catch (Exception exception)
        {
            RecordFailure(entry, host, exception);
        }
    }

    /// <summary>
    ///     This method stores a resolved address and logs the recovery after a warned streak.
    /// </summary>
    private void RecordSuccess(Entry entry, string host, IPAddress address)
    {
        bool recovered;

        lock (entry)
        {
            recovered = entry.Failures >= WarnAfterFailures;
            entry.Failures = 0;
            entry.IsRunning = false;
            entry.Sendable = entry.Parsed with { Address = address };
        }

        if (recovered)
            logger.LogInformation(
                "The forwarder host '{Host}' resolves again, to {Address}.",
                host,
                address
            );
        else
            logger.LogDebug("The forwarder host '{Host}' resolved to {Address}.", host, address);
    }

    /// <summary>
    ///     This method keeps the last good address and logs one Warning per failure streak.
    /// </summary>
    private void RecordFailure(Entry entry, string host, Exception exception)
    {
        int failures;

        lock (entry)
        {
            failures = ++entry.Failures;
            entry.IsRunning = false;
        }

        if (failures == WarnAfterFailures)
            logger.LogWarning(
                exception,
                "Looking up the forwarder host '{Host}' failed {Failures} times in a row; {State}.",
                host,
                failures,
                entry.Sendable is null
                    ? "the forwarder is skipped until the name resolves"
                    : "the last known address stays in use"
            );
        else
            logger.LogDebug(exception, "Looking up the forwarder host '{Host}' failed.", host);
    }

    /// <summary>
    ///     This class is the state of one definition string. Fields after the constructor are
    ///     read and written under <c>lock (entry)</c>, except <see cref="Sendable" /> and
    ///     <see cref="Lookup" />, which are replaced as whole references and read without the lock.
    /// </summary>
    private sealed class Entry
    {
        /// <summary>
        ///     This constructor stores the parse result; <see langword="null" /> when the string
        ///     does not parse.
        /// </summary>
        public Entry(UpstreamForwarderDefinition parsed)
        {
            Parsed = parsed;
            NeedsLookup =
                parsed is { Address: null, TlsHostName: not null }
                && parsed.Protocol != UpstreamForwarderProtocolEnum.Https;
            Sendable = NeedsLookup ? null : parsed;
        }

        /// <summary>The parse result, or <see langword="null" /> for an unparseable string.</summary>
        public UpstreamForwarderDefinition Parsed { get; }

        /// <summary>Whether the definition names a host that must be resolved.</summary>
        public bool NeedsLookup { get; }

        /// <summary>The definition with the latest good address, or <see langword="null" /> before the first one.</summary>
        public volatile UpstreamForwarderDefinition Sendable;

        /// <summary>The latest lookup task, for <see cref="WhenIdleAsync" />.</summary>
        public volatile Task Lookup;

        /// <summary>Whether a lookup is running.</summary>
        public bool IsRunning;

        /// <summary>When the latest lookup started.</summary>
        public DateTimeOffset? LastStartedAt;

        /// <summary>How many lookups failed in a row.</summary>
        public int Failures;
    }
}
