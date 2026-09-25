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
///     This class subscribes to <see cref="DnsQueryAnsweredEvent" /> and writes one
///     <see cref="DnsQueryLogEntryEntity" /> per answered query, which <c>QueryLogController</c>
///     searches.
///     <para>
///         <strong>Only the answered event is used</strong>, not <see cref="DnsQueryReceivedEvent" />.
///         <c>DnsQueryProcessingPipeline</c> publishes the answered event once per answered query,
///         with the timestamp, protocol, client address and question plus the outcome (response
///         code and answer count). Using both events would log each query twice or need them
///         correlated. Queries that are never answered (rate-limited, denied clients, malformed
///         messages) are not logged.
///     </para>
///     <para>
///         The write runs in a detached task rather than being awaited in
///         <see cref="HandleAsync" />: <c>DnsServerEventDispatcher</c> delivers events one at a
///         time, so a slow database write must not hold up other subscribers. The task opens its
///         own dependency injection scope, since the dispatcher's scope is disposed once every
///         subscriber has returned.
///     </para>
///     <para>
///         <b><see cref="QueryLoggingOptions.PrivacyLevel" /> is applied when the entry is
///         written</b>, not when it is read. A field the level removes is never stored, so no later
///         read can recover it. The level is read through <see cref="IOptionsMonitor{TOptions}" />,
///         so a change made through <c>QueryLoggingSettingsController</c> applies to the next query
///         without a restart. A separate "anonymize IPs" switch is not needed: that is
///         <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> on the same scale.
///     </para>
///     <para>
///         <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> anonymizes the address by
///         truncating it to its subnet (see <see cref="TruncateToSubnet" />), not by hashing it. An
///         unsalted hash of an IPv4 address is easy to reverse (there are only 2^32 addresses),
///         and a keyed hash would need a secret to manage. Truncation cannot be reversed, since
///         the masked bits are never stored, and still shows how much traffic comes from each
///         subnet.
///     </para>
///     <para>
///         <strong>Client identity is resolved when the entry is written.</strong> An address can
///         belong to another device later (a new DHCP lease, a laptop rejoining), so resolving a
///         logged address at read time could attribute history to the wrong client.
///         <see cref="DnsQueryLogEntryEntity.ClientIdentifier" /> and
///         <see cref="DnsQueryLogEntryEntity.ClientProtocolVersion" /> are filled from
///         <see cref="IClientIdentityResolver" />, which also unmaps IPv4-mapped IPv6 addresses.
///         The resolver is optional: without one, or for an address it cannot resolve, both fields
///         stay <see langword="null" />.
///     </para>
///     <para>
///         <strong>Client identity is stored only when the full address is.</strong> A MAC
///         address or DUID identifies a device at least as well as its address, and it has no
///         truncated form, so it is dropped from <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />
///         up, where the address is truncated, not only at
///         <see cref="QueryLogPrivacyLevelEnum.FullyAnonymous" />, where the address is dropped.
///         At those levels the resolver is not called at all.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to open a scope for the repository per entry.</param>
/// <param name="options">The on/off switch and the privacy level, read live.</param>
/// <param name="logger">The logger used to report a failed write.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver that turns a source address into a persistent client identifier.
///     With <see langword="null" /> (the default),
///     <see cref="DnsQueryLogEntryEntity.ClientIdentifier" /> and
///     <see cref="DnsQueryLogEntryEntity.ClientProtocolVersion" /> are never set.
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
        // With logging off, return before doing any work. Enable is read live, so turning it off
        // stops writes at once; turning it on when the subscriber was not registered at startup
        // still needs a restart (see ServiceCollectionQueryLogExtension).
        if (!options.CurrentValue.Enable)
            return ValueTask.CompletedTask;

        _ = PersistAsync(domainEvent, options.CurrentValue.PrivacyLevel, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method opens a scope, maps the event onto a new
    ///     <see cref="DnsQueryLogEntryEntity" />, redacts it for <paramref name="privacyLevel" /> and
    ///     saves it. <see cref="HandleAsync" /> reads <paramref name="privacyLevel" /> when the event
    ///     arrives rather than here, so a level change during a pending write does not apply a
    ///     different level to that query. A failure is logged, not thrown.
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

            // Domain names are removed from DomainRedacted up; each level is at least as private
            // as the one below it.
            bool redactDomain = privacyLevel >= QueryLogPrivacyLevelEnum.DomainRedacted;

            // Client identity is dropped from ClientAnonymized up (see the class summary).
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
    ///     This method returns what is stored for the client address at
    ///     <paramref name="privacyLevel" />: the address as is below
    ///     <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" />, its subnet at that level, and
    ///     <see langword="null" /> at <see cref="QueryLogPrivacyLevelEnum.FullyAnonymous" /> or
    ///     above.
    /// </summary>
    /// <param name="remoteAddress">The client's address, or <see langword="null" /> if unknown.</param>
    /// <param name="privacyLevel">The privacy level in effect for this query.</param>
    /// <returns>The text form to store, or <see langword="null" />.</returns>
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
    ///     This method resolves the client's persistent identifier when
    ///     <paramref name="privacyLevel" /> allows it (see the class summary). When it does not,
    ///     the resolver is not called.
    /// </summary>
    /// <param name="remoteAddress">The client's address, or <see langword="null" /> if unknown.</param>
    /// <param name="privacyLevel">The privacy level in effect for this query.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The resolution to store, or <see langword="null" /> when there is no resolver, no
    ///     address, the address cannot be resolved, or <paramref name="privacyLevel" /> drops
    ///     client identity.
    /// </returns>
    private async Task<ClientIdentityResolution> ResolveClientIdentityAsync(
        IPAddress remoteAddress,
        QueryLogPrivacyLevelEnum privacyLevel,
        CancellationToken stoppingToken
    )
    {
        // Dropped from ClientAnonymized up, the level at which RedactClientAddress starts
        // truncating the address.
        bool suppressed = privacyLevel >= QueryLogPrivacyLevelEnum.ClientAnonymized;

        if (suppressed || clientIdentityResolver is null || remoteAddress is null)
            return null;

        return await clientIdentityResolver.ResolveAsync(remoteAddress, stoppingToken);
    }

    /// <summary>
    ///     This method zeroes the host part of <paramref name="address" />: the last octet of an
    ///     IPv4 address (/24) or the last 64 bits of an IPv6 address (/64). The result names the
    ///     subnet, not the client. An IPv4-mapped IPv6 address is treated as IPv6, so its whole
    ///     IPv4 part is zeroed.
    /// </summary>
    private static IPAddress TruncateToSubnet(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();

        // IPv4 (4 bytes): zero the last octet. IPv6 (16 bytes): zero the last 8 bytes. The
        // prefix lengths are fixed rather than configurable, to keep the setting simple.
        int bytesToMask = bytes.Length == 4 ? 1 : 8;

        for (int i = bytes.Length - bytesToMask; i < bytes.Length; i++)
            bytes[i] = 0;

        return new IPAddress(bytes);
    }
}
