using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Dns.Server.Authoritative.Journal;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class publishes and removes the record the RFC 8555 §8.4 DNS-01 challenge needs: a
///     TXT record at <c>_acme-challenge.&lt;domain&gt;</c> holding the key authorization digest.
///     A wildcard domain (<c>*.example.com</c>) is validated at its base name, so its record is
///     at <c>_acme-challenge.example.com</c> (RFC 8555 §7.1.3, §8.4).
///     It works only for domains inside a zone this server hosts
///     (<see cref="AuthoritativeZoneCache.FindZone" />); any other domain is refused. The CA
///     queries the DNS for the TXT record (RFC 8555 §8.4) and may reach any of the zone's public
///     name servers, so the record is synced like
///     any record: a NOTIFY brings it to secondaries by zone transfer, and a CloudPrimary zone is
///     pushed to its provider at once through <see cref="ICloudZoneSyncTrigger" /> after the
///     publish and after the removal. Only <c>Primary</c> and <c>CloudPrimary</c> zones take a
///     challenge, because this server is not the source of truth for any other type.
///     <see cref="AcmeChallengePropagationWaiter" /> then waits until the servers serve the value.
/// </summary>
/// <param name="zoneCache">The cache used to find the zone that holds a domain.</param>
/// <param name="recordRepository">The repository the challenge records are looked up in for removal.</param>
/// <param name="zoneUpdateService">The service every add and remove goes through.</param>
/// <param name="logger">The logger this responder records a domain that is not locally hosted and a failed cloud push to.</param>
/// <param name="cloudSync">
///     The trigger that pushes a CloudPrimary zone to its provider at once. With
///     <see langword="null" />, a CloudPrimary zone waits for its regular sync.
/// </param>
public sealed class AcmeDnsChallengeResponder(
    AuthoritativeZoneCache zoneCache,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService,
    ILogger<AcmeDnsChallengeResponder> logger,
    ICloudZoneSyncTrigger cloudSync = null
)
{
    /// <summary>
    ///     This method publishes the DNS-01 challenge TXT record for a domain, with a 60-second
    ///     TTL. The record is synced like any other, and a CloudPrimary zone is pushed to its
    ///     provider before this method returns.
    /// </summary>
    /// <param name="domain">The domain a certificate is being requested for; a leading <c>*.</c> is allowed.</param>
    /// <param name="dnsTxtValue">The key authorization digest the CA computed, from <c>AcmeContext.AccountKey.DnsTxt(token)</c>.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="domain" /> is in a zone this server hosts and
    ///     the record was published, otherwise <see langword="false" />.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when the zone is neither a Primary nor a CloudPrimary zone: DNS-01 needs the
    ///     record on the zone's primary.
    /// </exception>
    public async Task<bool> PublishChallengeAsync(
        string domain,
        string dnsTxtValue,
        CancellationToken stoppingToken = default
    )
    {
        DnsName challengeName = BuildChallengeName(domain);
        CachedZone zone = zoneCache.FindZone(challengeName, DnsResourceRecordTypeEnum.Txt);

        if (zone is null)
        {
            logger.LogWarning(
                "'{Domain}' is not a zone this server hosts; cannot publish a DNS-01 challenge for it.",
                domain
            );
            return false;
        }

        if (zone.Zone.Type is not (ZoneTypeEnum.Primary or ZoneTypeEnum.CloudPrimary))
            throw new InvalidOperationException(
                $"DNS-01 needs the record on the zone's primary; this server holds a {zone.Zone.Type} copy of '{zone.Zone.Name}'."
            );

        DnsResourceRecord wireRecord = new(
            challengeName,
            DnsResourceRecordTypeEnum.Txt,
            DnsClassEnum.Internet,
            60,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Txt,
                new TxtRecordData([dnsTxtValue])
            )
        );

        RecordEntity record = RecordEntityCodec.FromWireRecord(wireRecord, zone.Zone.Id);

        // ExcludeFromSync stays false on purpose: the CA may ask a secondary or the cloud
        // provider, and both get the record only if it syncs like any other.
        await zoneUpdateService.ApplyChangesAsync(zone.Zone.Id, [record], [], null, stoppingToken);
        await PushCloudZoneAsync(zone.Zone, stoppingToken);
        return true;
    }

    /// <summary>
    ///     This method removes the TXT record this attempt published at
    ///     <c>_acme-challenge.&lt;domain&gt;</c> once the attempt is over. Only a record that holds
    ///     exactly one string equal to <paramref name="dnsTxtValue" /> is removed; a record an
    ///     operator added, or one another attempt published at the same name (for example a
    ///     wildcard and its base name), stays. It does nothing when there is no such record or the
    ///     domain is not in a hosted zone.
    /// </summary>
    /// <param name="domain">The domain the certificate was requested for; a leading <c>*.</c> is allowed.</param>
    /// <param name="dnsTxtValue">The value <see cref="PublishChallengeAsync" /> published.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task RemoveChallengeAsync(
        string domain,
        string dnsTxtValue,
        CancellationToken stoppingToken = default
    )
    {
        DnsName challengeName = BuildChallengeName(domain);
        CachedZone zone = zoneCache.FindZone(challengeName, DnsResourceRecordTypeEnum.Txt);

        if (zone is null)
            return;

        IReadOnlyList<RecordEntity> existingRecords = await recordRepository.FindByNameAsync(
            zone.Zone.Id,
            challengeName.ToString().TrimEnd('.'),
            DnsResourceRecordTypeEnum.Txt,
            stoppingToken
        );

        // Match on the value, not on the name: two attempts can share a name, and the zone can
        // hold TXT records this class never wrote.
        List<RecordEntity> ownRecords = existingRecords
            .Where(record =>
                RecordEntityCodec.DecodeTypedData(record) is TxtRecordData { Values.Count: 1 } txt
                && string.Equals(txt.Values[0], dnsTxtValue, StringComparison.Ordinal)
            )
            .ToList();

        if (ownRecords.Count == 0)
            return;

        await zoneUpdateService.ApplyChangesAsync(
            zone.Zone.Id,
            [],
            ownRecords,
            null,
            stoppingToken
        );
        await PushCloudZoneAsync(zone.Zone, stoppingToken);
    }

    /// <summary>
    ///     This method pushes a CloudPrimary zone to its provider at once, so the provider's name
    ///     servers serve the change before the CA asks. Other zone types need nothing: a NOTIFY
    ///     already carries the change to their secondaries. A failed push is logged and does not
    ///     fail the challenge, because the zone's regular sync still retries it and the
    ///     propagation wait reports a server that never served the value.
    /// </summary>
    private async Task PushCloudZoneAsync(ZoneEntity zone, CancellationToken stoppingToken)
    {
        if (zone.Type != ZoneTypeEnum.CloudPrimary || cloudSync is null)
            return;

        try
        {
            await cloudSync.SyncZoneNowAsync(zone.Id, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "The cloud push of zone '{ZoneName}' after a DNS-01 change failed; its regular sync tries again.",
                zone.Name
            );
        }
    }

    /// <summary>
    ///     This method returns the name the CA validates for <paramref name="domain" />:
    ///     <c>_acme-challenge.</c> in front of the domain, or in front of a wildcard's base name
    ///     (RFC 8555 §7.1.3, §8.4).
    /// </summary>
    private static DnsName BuildChallengeName(string domain)
    {
        string name = domain.Trim().TrimEnd('.');

        if (name.StartsWith("*.", StringComparison.Ordinal))
            name = name[2..];

        return DnsName.Parse(name).PrependLabels("_acme-challenge");
    }
}
