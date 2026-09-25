using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This interface is implemented once per cloud DNS provider a zone can sync with
///     (<c>CloudflareDnsProvider</c>, <c>Route53DnsProvider</c>, <c>DigitalOceanDnsProvider</c>). It
///     hides each provider's REST API behind the small contract <see cref="CloudDnsZoneSyncService" />
///     uses.
///     <para>
///     Only the record types all three providers support are synced
///     (<see cref="CloudDnsSyncSupportedRecordTypes.Types" />): A, AAAA, CNAME, MX, NS and TXT,
///     mapped by <see cref="CloudDnsRecordMapper.ToWireRecord" /> and
///     <see cref="CloudDnsRecordMapper.FromWireRecord" />, plus SRV and CAA, which each provider
///     represents differently and which have their mapper methods.
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
    /// <param name="credentials">The decrypted credential fields.</param>
    /// <param name="providerZoneId">
    ///     The zone's ID at the provider (a Cloudflare zone ID, a Route 53 hosted zone ID, or a
    ///     DigitalOcean domain name).
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task<IReadOnlyList<CloudDnsProviderRecord>> ListRecordsAsync(
        IReadOnlyDictionary<string, string> credentials,
        string providerZoneId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method publishes a new record. The caller replaces a changed record by deleting and
    ///     re-creating it, so no mapping of local records to provider IDs has to be stored.
    /// </summary>
    /// <param name="credentials">The decrypted credential fields.</param>
    /// <param name="providerZoneId">The zone's ID at the provider.</param>
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
    /// <param name="credentials">The decrypted credential fields.</param>
    /// <param name="providerZoneId">The zone's ID at the provider.</param>
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
///     This class lists the record types every <see cref="ICloudDnsProvider" /> supports; see
///     <see cref="ICloudDnsProvider" />.
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
