using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This interface is implemented once per cloud DNS provider Ninlil can sync a
///     <see cref="Data.Abstractions.Entity.ZoneEntity" /> against
///     (<c>CloudflareDnsProvider</c>/<c>Route53DnsProvider</c>/<c>DigitalOceanDnsProvider</c>),
///     hiding that provider's own REST API shape behind one small, uniform contract
///     <see cref="CloudDnsZoneSyncService" /> reconciles against, the cloud-provider analogue of
///     how <see cref="Transfer.IZoneTransferClient" /> hides AXFR/IXFR's own wire protocol.
///     <para>
///         Scoped to the record types every one of the three providers' documented REST APIs can
///         represent (see <see cref="CloudDnsSyncSupportedRecordTypes.Types" />): the six simple
///         name/TTL/single-value types -- A, AAAA, CNAME, MX, NS, TXT -- mapped generically by
///         <see cref="CloudDnsRecordMapper.ToWireRecord" />/<see cref="CloudDnsRecordMapper.FromWireRecord" />,
///         plus SRV and CAA, each of which needs its own structured, multi-field mapping per
///         provider (Cloudflare's own nested <c>data</c> object, DigitalOcean's own flat
///         <c>priority</c>/<c>port</c>/<c>weight</c>/<c>flags</c>/<c>tag</c> fields, Route 53's
///         own space-separated single-string <c>Value</c> convention) rather than one shared
///         generic path -- see <see cref="CloudDnsRecordMapper.ToSrvWireRecord" />/
///         <see cref="CloudDnsRecordMapper.FromSrvWireRecord" /> and
///         <see cref="CloudDnsRecordMapper.ToCaaWireRecord" />/
///         <see cref="CloudDnsRecordMapper.FromCaaWireRecord" />, and each provider's own
///         implementation of this interface for the wire-format detail.
///     </para>
/// </summary>
public interface ICloudDnsProvider
{
    /// <summary>
    ///     This property contains which provider this implementation talks to.
    /// </summary>
    CloudDnsProviderTypeEnum ProviderType { get; }

    /// <summary>
    ///     This method lists every currently published record for a zone.
    /// </summary>
    /// <param name="credentials">This provider's own decrypted credential fields.</param>
    /// <param name="providerZoneId">
    ///     The zone's own identifier at this provider (a Cloudflare zone ID, a Route 53 hosted
    ///     zone ID, or a DigitalOcean domain name).
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task<IReadOnlyList<CloudDnsProviderRecord>> ListRecordsAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method publishes a new record. A record whose content has changed is deleted
    ///     and recreated by the caller, rather than updated in place -- simpler, and avoids
    ///     needing to persist a local-record-to-provider-record-id mapping anywhere.
    /// </summary>
    /// <param name="credentials">This provider's own decrypted credential fields.</param>
    /// <param name="providerZoneId">The zone's own identifier at this provider.</param>
    /// <param name="record">The record to publish.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task CreateRecordAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        DnsResourceRecord record,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method removes a previously published record.
    /// </summary>
    /// <param name="credentials">This provider's own decrypted credential fields.</param>
    /// <param name="providerZoneId">The zone's own identifier at this provider.</param>
    /// <param name="existingRecord">The record to remove, as returned by <see cref="ListRecordsAsync" />.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task DeleteRecordAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        CloudDnsProviderRecord existingRecord,
        CancellationToken stoppingToken = default
    );
}

/// <summary>
///     This class contains the fixed set of record types every <see cref="ICloudDnsProvider" />
///     implementation supports -- see the scoping note on <see cref="ICloudDnsProvider" /> itself.
/// </summary>
public static class CloudDnsSyncSupportedRecordTypes
{
    /// <summary>
    ///     This property contains the supported record types.
    /// </summary>
    public static readonly IReadOnlySet<DnsResourceRecordTypeEnum> Types =
        new HashSet<DnsResourceRecordTypeEnum>
        {
            DnsResourceRecordTypeEnum.A,
            DnsResourceRecordTypeEnum.Aaaa,
            DnsResourceRecordTypeEnum.Cname,
            DnsResourceRecordTypeEnum.MX,
            DnsResourceRecordTypeEnum.NS,
            DnsResourceRecordTypeEnum.Txt,
            DnsResourceRecordTypeEnum.Srv,
            DnsResourceRecordTypeEnum.Caa,
        };
}
