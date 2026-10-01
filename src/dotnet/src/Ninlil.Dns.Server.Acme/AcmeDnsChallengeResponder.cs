using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class publishes and removes the record the RFC 8555 §8.4 DNS-01 challenge needs: a
///     TXT record at <c>_acme-challenge.&lt;domain&gt;</c> holding the key authorization digest.
///     It works only for domains inside a zone this server hosts
///     (<see cref="AuthoritativeZoneCache.FindZone" />); any other domain is refused. The
///     record is marked <c>ExcludeFromSync</c>, so zone transfers and cloud sync never carry
///     it, and the challenge can only succeed if the CA queries this server.
/// </summary>
/// <param name="zoneCache">The cache used to find the zone that holds a domain.</param>
/// <param name="recordRepository">The repository the challenge records are looked up in for removal.</param>
/// <param name="zoneUpdateService">The service every add and remove goes through.</param>
/// <param name="logger">The logger this responder records a domain that is not locally hosted to.</param>
public sealed class AcmeDnsChallengeResponder(
    AuthoritativeZoneCache zoneCache,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService,
    ILogger<AcmeDnsChallengeResponder> logger
)
{
    /// <summary>
    ///     This method publishes the DNS-01 challenge TXT record for a domain, with a 60-second
    ///     TTL.
    /// </summary>
    /// <param name="domain">The domain a certificate is being requested for.</param>
    /// <param name="dnsTxtValue">The key authorization digest the CA computed, from <c>AcmeContext.AccountKey.DnsTxt(token)</c>.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="domain" /> is in a zone this server hosts and
    ///     the record was published, otherwise <see langword="false" />.
    /// </returns>
    public async Task<bool> PublishChallengeAsync(
        string domain,
        string dnsTxtValue,
        CancellationToken stoppingToken = default
    )
    {
        DnsName challengeName = DnsName.Parse($"_acme-challenge.{domain}");
        CachedZone zone = zoneCache.FindZone(challengeName, DnsResourceRecordTypeEnum.Txt);

        if (zone is null)
        {
            logger.LogWarning(
                "'{Domain}' is not a zone this server hosts; cannot publish a DNS-01 challenge for it.",
                domain
            );
            return false;
        }

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

        // The record is temporary and system-managed, so it is kept out of zone transfers and
        // cloud sync.
        record.ExcludeFromSync = true;

        await zoneUpdateService.ApplyChangesAsync(zone.Zone.Id, [record], [], null, stoppingToken);
        return true;
    }

    /// <summary>
    ///     This method removes every TXT record at <c>_acme-challenge.&lt;domain&gt;</c> once the
    ///     attempt is over, including any not published by <see cref="PublishChallengeAsync" />.
    ///     It does nothing when there are none or the domain is not in a hosted zone.
    /// </summary>
    public async Task RemoveChallengeAsync(string domain, CancellationToken stoppingToken = default)
    {
        DnsName challengeName = DnsName.Parse($"_acme-challenge.{domain}");
        CachedZone zone = zoneCache.FindZone(challengeName, DnsResourceRecordTypeEnum.Txt);

        if (zone is null)
            return;

        IReadOnlyList<RecordEntity> existingRecords = await recordRepository.FindByNameAsync(
            zone.Zone.Id,
            challengeName.ToString().TrimEnd('.'),
            DnsResourceRecordTypeEnum.Txt,
            stoppingToken
        );

        if (existingRecords.Count == 0)
            return;

        await zoneUpdateService.ApplyChangesAsync(
            zone.Zone.Id,
            [],
            existingRecords,
            null,
            stoppingToken
        );
    }
}
