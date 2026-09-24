using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog;

/// <summary>
///     This class subscribes to <see cref="DnsQueryAnsweredEvent" /> and persists a row for
///     every query it observes, turning the dispatcher's own already-published event stream into
///     the searchable historical record <c>QueryLogController</c> serves.
///     <para>
///         <strong>Only the answered event is subscribed to, not <see cref="DnsQueryReceivedEvent" /></strong>:
///         every return path through the dispatcher publishes an answered event exactly once per
///         query, and it already carries everything the received event does (the same timestamp,
///         protocol, client address, and question) plus the actual outcome (response code and
///         answer count) the received event has no way to know yet. Subscribing to both would
///         either log every query twice or force this subscriber to correlate two separate events
///         for no benefit.
///     </para>
///     <para>
///         Persistence runs in a detached background task rather than being awaited directly from
///         <see cref="HandleAsync" />, mirroring every other event-triggered write path in this
///         project (architectural invariant #3): the event dispatcher processes one event at a
///         time, and a slow database write must never stall every other subscriber's own handling
///         of every other event. The background task opens its own dependency injection scope for
///         the same reason -- the scope <see cref="HandleAsync" /> was invoked in is disposed as
///         soon as every subscriber's own handling of this event completes, well before this
///         detached write is actually done.
///     </para>
///     <para>
///         <b><see cref="QueryLoggingOptions.PrivacyLevel" /> is applied right here, at write
///         time</b> -- never as a read-time display filter. A field this level's own redaction
///         drops is never even placed on <see cref="DnsQueryLogEntryEntity" /> before it is
///         handed to the repository, so a later read (at any privacy level, including
///         <see cref="QueryLogPrivacyLevelEnum.Full" />) can never recover a value that was never
///         actually stored -- there is nothing reversible sitting in the database for it to
///         recover. This class reads the option via <see cref="IOptionsMonitor{TOptions}" /> (not
///         the usual, snapshotted <see cref="IOptions{TOptions}" />) specifically so a level
///         change an operator makes through <c>Controllers.QueryLoggingSettingsController</c>
///         takes effect on the very next query answered, with no restart -- exactly the
///         "graduated, live-changeable" scale this feature is meant to be, and precisely why a
///         standalone, separately-toggled "anonymize IPs" setting is unnecessary: it is just
///         <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> (or higher) on this same
///         scale, one setting to reason about instead of two.
///     </para>
///     <para>
///         <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />'s own client-anonymization
///         mechanism is subnet truncation (masking an IPv4 address's own trailing octet, or an
///         IPv6 address's own trailing 64 bits, to zero), not a hash. An unsalted hash of an IPv4
///         address is not meaningfully more private than storing it in the clear -- the entire
///         IPv4 address space is only 2^32 values, cheap to reverse with a single precomputed
///         table -- and a salted/keyed hash would need this feature to manage a secret key it
///         otherwise has no reason to have. Truncation, by contrast, is trivially irreversible (the
///         masked bits are never stored at all, so there is nothing to invert) and keeps exactly
///         the kind of aggregate detail an operator actually wants at this level -- "how much
///         traffic is coming from this subnet" -- which a hash's own pseudo-random output would
///         have thrown away just as completely as full anonymization does.
///     </para>
///     <para>
///         <strong>Client identity is resolved and stamped here too, at write time, never at read
///         time.</strong> A source address logged today may belong to a completely different
///         physical device tomorrow (a DHCP lease reassignment, a laptop rejoining the network
///         with a new address) -- re-running identity resolution against a stale logged address
///         at read time would silently misattribute history, so
///         <see cref="DnsQueryLogEntryEntity.ClientIdentifier" />/
///         <see cref="DnsQueryLogEntryEntity.ClientProtocolVersion" /> are resolved once,
///         right here, via the exact same <see cref="IClientIdentityResolver" /> every other
///         Phase 15 engine already consults (including the mandatory IPv4-mapped-address
///         unmapping bug catalog item #27 documents -- performed inside the resolver itself, not
///         by this class). <paramref name="clientIdentityResolver" /> is optional, exactly like
///         <c>Server.Filtering.DnsFilterEngine</c>'s own identical dependency: <see langword="null" />
///         (no identity-resolution infrastructure wired up) or an unresolvable address both leave
///         both fields <see langword="null" />, never an exception.
///     </para>
///     <para>
///         <strong>The identical privacy gate <see cref="RedactClientAddress" /> already applies
///         to <see cref="DnsQueryLogEntryEntity.ClientAddress" /> is reused, unchanged, for
///         client identity too</strong> -- a persistent MAC address/DUID is at least as
///         identifying as a raw source address (arguably more so, since it survives an address
///         change), so it is suppressed starting at the exact same level
///         (<see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />) that first anonymizes
///         <see cref="DnsQueryLogEntryEntity.ClientAddress" />, rather than only at
///         <see cref="QueryLogPrivacyLevelEnum.FullyAnonymous" /> where the address is dropped
///         entirely -- a truncated-subnet address at <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />
///         still describes a whole neighborhood of devices, not one, but a persistent client
///         identifier has no equivalent "truncated" form that keeps the same property, so it is
///         suppressed outright the moment the address itself is first anonymized. This is a single
///         shared gate, computed once and reused for both fields, deliberately -- never a second,
///         independent privacy decision that could quietly drift from the first. The resolver is
///         never even invoked once the gate is already closed, so turning this level up also
///         genuinely stops the extra resolution work, not merely its result.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped repository per event.</param>
/// <param name="options">This subscriber's own configured on/off switch and live privacy level.</param>
/// <param name="logger">The logger this subscriber records unexpected persistence failures to.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver used to derive a query's own persistent client identifier from its
///     source address -- cross-cutting infrastructure exactly like
///     <c>Server.Filtering.DnsFilterEngine</c>'s own identical dependency, so every existing
///     caller/test constructing this subscriber without one keeps compiling and behaving
///     unchanged. <see langword="null" /> (the default) always leaves
///     <see cref="DnsQueryLogEntryEntity.ClientIdentifier" />/
///     <see cref="DnsQueryLogEntryEntity.ClientProtocolVersion" /> unset.
/// </param>
public sealed class DnsQueryLogSubscriber(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<QueryLoggingOptions> options,
    ILogger<DnsQueryLogSubscriber> logger,
    IClientIdentityResolver clientIdentityResolver = null
) : IDnsServerEventSubscriber<DnsQueryAnsweredEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DnsQueryAnsweredEvent domainEvent, CancellationToken stoppingToken)
    {
        // When query logging is turned off, this returns immediately without opening a scope,
        // resolving a repository, or doing any other work at all -- a query log entry is never
        // even constructed, let alone discarded, so disabling this feature genuinely costs
        // nothing on the hot query-answering path beyond this one property read. Because this is
        // now read from a live IOptionsMonitor rather than a startup-frozen IOptions snapshot,
        // flipping Enable to false while this subscriber is already registered stops new writes
        // immediately too -- a pleasant side effect of the same live-read machinery
        // PrivacyLevel needs, though (unlike PrivacyLevel) flipping Enable back on when this
        // subscriber was never registered in the first place still needs a restart, since the
        // registration decision itself is still made once, at startup, by
        // Extension.ServiceCollectionQueryLogExtension.
        if (!options.CurrentValue.Enable)
            return ValueTask.CompletedTask;

        _ = PersistAsync(domainEvent, options.CurrentValue.PrivacyLevel, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method does the actual work: opening a fresh scope, mapping the event onto a new
    ///     <see cref="DnsQueryLogEntryEntity" />, redacting it per <paramref name="privacyLevel" />,
    ///     and committing it. <paramref name="privacyLevel" /> is captured by
    ///     <see cref="HandleAsync" /> at the moment the query was actually answered, rather than
    ///     re-read here, so a level change racing a detached, in-flight persistence task can never
    ///     apply the *new* level to a query that was actually answered under the *old* one.
    /// </summary>
    private async Task PersistAsync(
        DnsQueryAnsweredEvent domainEvent,
        QueryLogPrivacyLevelEnum privacyLevel,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IDnsQueryLogRepository repository =
                scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            // Domain names are redacted starting at DomainRedacted (level 1) and remain redacted
            // at every level above it -- each level is strictly more private than the last, never
            // less.
            bool redactDomain = privacyLevel >= QueryLogPrivacyLevelEnum.DomainRedacted;

            // Client identity is resolved -- and, at the exact same gate, suppressed -- via the
            // same single, shared decision RedactClientAddress already makes for the address
            // itself: see this class's own doc comment for why a persistent identifier is
            // suppressed starting at ClientAnonymized, not only at FullyAnonymous.
            ClientIdentityResolution resolution = await ResolveClientIdentityAsync(
                domainEvent.RemoteAddress,
                privacyLevel,
                stoppingToken
            );

            DnsQueryLogEntryEntity entry = new()
            {
                Id = Guid.NewGuid(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                OccurredAtUtc = domainEvent.OccurredAtUtc,
                ClientAddress = RedactClientAddress(domainEvent.RemoteAddress, privacyLevel),
                Protocol = domainEvent.Protocol,
                QueriedName = redactDomain ? null : domainEvent.QueriedName,
                QueriedType = domainEvent.QueriedType,
                ResponseCode = domainEvent.ResponseCode,
                AnswerCount = domainEvent.AnswerCount,
                ClientIdentifier = resolution?.ClientIdentifier,
                ClientProtocolVersion = resolution?.ProtocolVersion,
            };

            await repository.AddAsync(entry, stoppingToken);
            await unitOfWork.SaveChangesAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Unhandled exception while persisting a query log entry.");
        }
    }

    /// <summary>
    ///     This method resolves what -- if anything -- actually gets stored for the querying
    ///     client's own address, under <paramref name="privacyLevel" />: the address verbatim
    ///     below <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />, a subnet-truncated form
    ///     at exactly that level, and nothing at all (<see langword="null" />) at
    ///     <see cref="QueryLogPrivacyLevelEnum.FullyAnonymous" /> or above.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own real address, or <see langword="null" /> if none was known.</param>
    /// <param name="privacyLevel">The privacy level in effect for this query.</param>
    /// <returns>The textual presentation form actually written to storage, or <see langword="null" />.</returns>
    private static string RedactClientAddress(
        IPAddress remoteAddress,
        QueryLogPrivacyLevelEnum privacyLevel
    )
    {
        if (remoteAddress is null)
            return null;

        if (privacyLevel >= QueryLogPrivacyLevelEnum.FullyAnonymous)
            return null;

        if (privacyLevel < QueryLogPrivacyLevelEnum.ClientAnonymized)
            return remoteAddress.ToString();

        return TruncateToSubnet(remoteAddress).ToString();
    }

    /// <summary>
    ///     This method resolves the querying client's own persistent identifier, subject to the
    ///     exact same privacy gate <see cref="RedactClientAddress" /> already applies to the raw
    ///     address itself -- see this class's own doc comment for the full reasoning. No resolver
    ///     call is made at all once <paramref name="privacyLevel" /> has already closed the gate,
    ///     so raising this level genuinely stops the extra resolution work, not merely its
    ///     stored result.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own real address, or <see langword="null" /> if none was known.</param>
    /// <param name="privacyLevel">The privacy level in effect for this query.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The resolution to stamp onto this entry, or <see langword="null" /> when no resolver is
    ///     configured, the address is unresolvable, no address was known at all, or
    ///     <paramref name="privacyLevel" /> already suppresses client identity for this entry.
    /// </returns>
    private async Task<ClientIdentityResolution> ResolveClientIdentityAsync(
        IPAddress remoteAddress,
        QueryLogPrivacyLevelEnum privacyLevel,
        CancellationToken stoppingToken
    )
    {
        // Suppressed starting at exactly the same level RedactClientAddress first anonymizes the
        // raw address at (ClientAnonymized) -- see this class's own doc comment for why a
        // persistent identifier has no equivalent "truncated" middle ground the way an address
        // does, and so is suppressed outright rather than only at FullyAnonymous.
        bool suppressed = privacyLevel >= QueryLogPrivacyLevelEnum.ClientAnonymized;

        if (suppressed || clientIdentityResolver is null || remoteAddress is null)
            return null;

        return await clientIdentityResolver.ResolveAsync(remoteAddress, stoppingToken);
    }

    /// <summary>
    ///     This method masks <paramref name="address" />'s own trailing bits to zero -- the last
    ///     octet for an IPv4 address (a /24 truncation), or the trailing 64 bits for an IPv6
    ///     address (a /64 truncation) -- returning a new address that identifies only the
    ///     containing subnet, never the individual client within it. The masked bits are computed
    ///     from, but never derived back into, the original address -- there is no way to recover
    ///     them from the result.
    /// </summary>
    private static IPAddress TruncateToSubnet(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        // IPv4 (4 bytes): zero the last octet, a /24 truncation. IPv6 (16 bytes): zero the
        // trailing 8 bytes (64 bits), a /64 truncation. Both mask counts are fixed, not
        // operator-configurable -- see this project's own known-limitations note on CAA/PTR
        // reconciliation's identical fixed /24 and /64 choice for the same "simple, predictable,
        // good enough at this project's homelab/small-org scale" reasoning.
        int bytesToMask = bytes.Length == 4 ? 1 : 8;

        for (int i = bytes.Length - bytesToMask; i < bytes.Length; i++)
            bytes[i] = 0;

        return new IPAddress(bytes);
    }
}
