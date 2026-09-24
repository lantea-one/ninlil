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
///     This class publishes and removes the one DNS record RFC 8555 §8.4's DNS-01 challenge
///     needs: a TXT record at <c>_acme-challenge.&lt;domain&gt;</c> carrying the CA-computed key
///     authorization digest. Scoped, deliberately, to domains this server is itself
///     authoritative for -- <see cref="AuthoritativeZoneCache.FindZone" /> is checked first, and
///     a domain that is not actually locally hosted is refused rather than silently doing
///     nothing useful.
/// </summary>
/// <param name="zoneCache">The cache used to confirm a domain is actually locally hosted.</param>
/// <param name="recordRepository">The repository any previously published challenge record is looked up through, to remove it.</param>
/// <param name="zoneUpdateService">The service every add/remove is actually applied through.</param>
/// <param name="logger">The logger this responder records a domain that is not locally hosted to.</param>
public sealed class AcmeDnsChallengeResponder(
    AuthoritativeZoneCache zoneCache,
    IRecordRepository recordRepository,
    IZoneUpdateService zoneUpdateService,
    ILogger<AcmeDnsChallengeResponder> logger
)
{
    /// <summary>
    ///     This method publishes a domain's own DNS-01 challenge TXT record.
    /// </summary>
    /// <param name="domain">The domain a certificate is being requested for.</param>
    /// <param name="dnsTxtValue">The key authorization digest the CA computed, from <c>AcmeContext.AccountKey.DnsTxt(token)</c>.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether <paramref name="domain" /> is actually a
    ///     zone this server hosts -- publishing only ever happens when this is <see langword="true" />.
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

        // This record is entirely ephemeral, system-managed state -- never something an
        // operator authored, never something a secondary or cloud provider should ever learn
        // about via a sync in either direction.
        record.ExcludeFromSync = true;

        await zoneUpdateService.ApplyChangesAsync(zone.Zone.Id, [record], [], null, stoppingToken);
        return true;
    }

    /// <summary>
    ///     This method removes a domain's own previously published DNS-01 challenge TXT
    ///     record(s), once the CA has finished validating it (or the attempt has failed) -- a
    ///     no-op if <see cref="PublishChallengeAsync" /> was never called, or the domain is not
    ///     locally hosted.
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
