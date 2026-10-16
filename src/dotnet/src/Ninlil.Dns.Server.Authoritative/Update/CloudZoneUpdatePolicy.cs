using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.CloudSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Update;

/// <summary>
///     This class holds the rule that decides which records an RFC 2136 dynamic update may add to a
///     <see cref="ZoneTypeEnum.CloudPrimary" /> zone. <see cref="DynamicUpdateQueryResolver" />
///     applies it to the records an update would add, and its tests call it directly.
/// </summary>
internal static class CloudZoneUpdatePolicy
{
    /// <summary>
    ///     This method tells whether a record added to a CloudPrimary zone either reaches the cloud
    ///     provider or is local by design. A type outside
    ///     <see cref="CloudDnsSyncSupportedRecordTypes.Types" /> (PTR, NAPTR, TLSA and so on) is
    ///     served by this server but never pushed, so the update client would believe the public
    ///     servers answer with it when they do not. The apex SOA and the apex NS set are local by
    ///     design: the provider owns its own, and <see cref="CloudDnsZoneSyncService" /> neither
    ///     pushes nor deletes them. An update to them changes only what this server serves.
    /// </summary>
    /// <param name="record">The record an update would add.</param>
    /// <param name="zoneName">The zone's apex name (a trailing dot is ignored).</param>
    /// <returns><see langword="true" /> when the record may be added to the zone.</returns>
    internal static bool IsPushable(RecordEntity record, string zoneName)
    {
        if (CloudDnsSyncSupportedRecordTypes.Types.Contains(record.Type))
            return true;

        // The apex NS set is in the supported types already; only the apex SOA needs the
        // exception. A SOA below the apex is not a valid record, so it gets no exception.
        return record.Type == DnsResourceRecordTypeEnum.Soa
            && string.Equals(
                record.Name.TrimEnd('.'),
                zoneName.TrimEnd('.'),
                StringComparison.OrdinalIgnoreCase
            );
    }
}
