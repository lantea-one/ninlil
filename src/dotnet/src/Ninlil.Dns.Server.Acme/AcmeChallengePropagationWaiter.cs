using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class waits, before the CA is asked to validate, until the DNS-01 TXT value is served
///     by this server's zone cache and by every other server that may answer for the zone: the
///     addresses in the zone's <c>NotifyAddresses</c> and the addresses of its apex NS hosts. The
///     CA queries the DNS for the TXT record (RFC 8555 §8.4), so it may ask any of them, and a
///     secondary or a cloud provider receives the
///     record a moment after <see cref="AcmeDnsChallengeResponder" /> publishes it.
///     <see cref="AcmeCertificateIssuer" /> calls it between the publish and the validation. The
///     wait is bounded by <see cref="Timeout" />: on timeout it logs one Warning naming the servers
///     that never served the value and returns <see langword="false" />, and the issuer validates
///     anyway, because a server that cannot be reached from here may still answer the CA.
/// </summary>
/// <param name="zoneCache">The cache that holds the zone, its NS records and its glue.</param>
/// <param name="probe">The probe that asks one server for the value.</param>
/// <param name="nameServerResolver">The resolver for NS hosts outside the zone.</param>
/// <param name="logger">The logger this waiter records skipped hosts and the timeout to.</param>
/// <param name="timeProvider">
///     The clock and timer source, so a test moves time by hand. With <see langword="null" />, the
///     system clock is used.
/// </param>
public sealed class AcmeChallengePropagationWaiter(
    AuthoritativeZoneCache zoneCache,
    IAcmeChallengeProbe probe,
    IAcmeNameServerAddressResolver nameServerResolver,
    ILogger<AcmeChallengePropagationWaiter> logger,
    TimeProvider timeProvider = null
)
{
    /// <summary>
    ///     This field contains how long the wait lasts at most before the issuer validates anyway.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     This field contains how long the wait sleeps between two rounds of probes.
    /// </summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This field contains the clock and timer source.
    /// </summary>
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This method waits until every server serves the value or <see cref="Timeout" /> passes.
    ///     The zone is the one that holds <paramref name="challengeName" /> in the zone cache.
    /// </summary>
    /// <param name="challengeName">The challenge name, <c>_acme-challenge.&lt;domain&gt;</c>.</param>
    /// <param name="dnsTxtValue">The key authorization digest the responder published.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> when every server served the value, <see langword="false" />
    ///     when the wait timed out.
    /// </returns>
    public async Task<bool> WaitAsync(
        DnsName challengeName,
        string dnsTxtValue,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset deadline = _timeProvider.GetUtcNow() + Timeout;
        List<IPAddress> pending = null;
        bool localServes = false;

        while (true)
        {
            CachedZone zone = zoneCache.FindZone(challengeName, DnsResourceRecordTypeEnum.Txt);

            // This server's own cache comes first: the other servers can only receive a record
            // the zone has already accepted, and a probe of them before then is wasted.
            localServes = zone is not null && ServesValue(zone, challengeName, dnsTxtValue);

            if (localServes)
            {
                // Resolved once the zone is known and held, so a slow lookup is not repeated.
                pending ??= await FindTargetsAsync(zone, stoppingToken);

                List<IPAddress> stillPending = [];

                foreach (IPAddress target in pending)
                    if (!await ProbeAsync(target, challengeName, dnsTxtValue, stoppingToken))
                        stillPending.Add(target);

                pending = stillPending;

                if (pending.Count == 0)
                    return true;
            }

            // The last round runs at the deadline, so a value that arrives in the final seconds
            // still counts.
            if (_timeProvider.GetUtcNow() >= deadline)
                break;

            await Task.Delay(PollInterval, _timeProvider, stoppingToken);
        }

        logger.LogWarning(
            "The DNS-01 challenge value at '{ChallengeName}' was not served by {Servers} within {Timeout}; "
                + "validation continues anyway.",
            challengeName,
            localServes && pending is not null
                ? string.Join(", ", pending)
                : "this server's own zone cache",
            Timeout
        );

        return false;
    }

    /// <summary>
    ///     This method reports whether the zone cache holds a TXT record at the challenge name with
    ///     exactly the one string <paramref name="value" />.
    /// </summary>
    private static bool ServesValue(CachedZone zone, DnsName name, string value) =>
        zone.GetRecords(name, DnsResourceRecordTypeEnum.Txt)
            .Any(record =>
                RecordEntityCodec.DecodeTypedData(record) is TxtRecordData { Values.Count: 1 } txt
                && string.Equals(txt.Values[0], value, StringComparison.Ordinal)
            );

    /// <summary>
    ///     This method lists the addresses to probe: the zone's <c>NotifyAddresses</c> that parse
    ///     as IP addresses and every address of every apex NS host, without duplicates. An NS host
    ///     inside the zone is read from the zone's own A and AAAA records (its glue); any other host
    ///     goes through the resolver. A host that cannot be resolved is skipped with a Debug log,
    ///     since one broken delegation must not block a certificate.
    /// </summary>
    private async Task<List<IPAddress>> FindTargetsAsync(
        CachedZone zone,
        CancellationToken stoppingToken
    )
    {
        List<IPAddress> targets = [];

        foreach (string text in zone.Zone.NotifyAddresses)
            if (IPAddress.TryParse(text?.Trim(), out IPAddress address))
                targets.Add(address);

        foreach (
            RecordEntity nsRecord in zone.GetRecords(zone.ApexName, DnsResourceRecordTypeEnum.NS)
        )
        {
            if (RecordEntityCodec.DecodeTypedData(nsRecord) is not NsRecordData ns)
                continue;

            List<IPAddress> glue = [];

            foreach (
                DnsResourceRecordTypeEnum type in new[]
                {
                    DnsResourceRecordTypeEnum.A,
                    DnsResourceRecordTypeEnum.Aaaa,
                }
            )
            foreach (RecordEntity record in zone.GetRecords(ns.NameServer, type))
                switch (RecordEntityCodec.DecodeTypedData(record))
                {
                    case ARecordData a:
                        glue.Add(a.Address);
                        break;
                    case AaaaRecordData aaaa:
                        glue.Add(aaaa.Address);
                        break;
                }

            if (glue.Count > 0)
            {
                targets.AddRange(glue);
                continue;
            }

            try
            {
                targets.AddRange(
                    await nameServerResolver.ResolveAsync(ns.NameServer, stoppingToken)
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(
                    exception,
                    "The name server '{Host}' of zone '{Zone}' cannot be resolved; it is not checked for the DNS-01 value.",
                    ns.NameServer,
                    zone.Zone.Name
                );
            }
        }

        return [.. targets.Distinct()];
    }

    /// <summary>
    ///     This method probes one server. A probe that throws counts as "not yet", because a
    ///     network error now says nothing about the next round.
    /// </summary>
    private async Task<bool> ProbeAsync(
        IPAddress target,
        DnsName name,
        string value,
        CancellationToken stoppingToken
    )
    {
        try
        {
            return await probe.ServesValueAsync(target, name, value, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Probing {Server} for the DNS-01 value failed.", target);
            return false;
        }
    }
}
