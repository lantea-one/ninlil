using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class decides whether a cached zone still matches the rows stored for it.
///     <see cref="ZoneCacheBackstopService" /> asks it once per zone and pass. The comparison
///     covers the zone fields the caches and resolvers read and every stored record by id and
///     content, and leaves out timestamps, audit columns and comments, which no answer depends on.
/// </summary>
internal static class ZoneChangeDetector
{
    /// <summary>
    ///     This method compares a cached zone with the stored rows of the same zone.
    /// </summary>
    /// <param name="cached">The cached zone.</param>
    /// <param name="stored">The stored zone row.</param>
    /// <param name="storedRecords">Every stored record of the zone, enabled or not.</param>
    /// <returns><see langword="true" /> if nothing the caches use differs.</returns>
    public static bool IsUnchanged(
        CachedZone cached,
        ZoneEntity stored,
        IReadOnlyList<RecordEntity> storedRecords
    ) => ZonesMatch(cached.Zone, stored) && RecordsMatch(cached.StoredRecords, storedRecords);

    /// <summary>
    ///     This method compares the zone fields the caches use.
    /// </summary>
    private static bool ZonesMatch(ZoneEntity left, ZoneEntity right) =>
        left.Id == right.Id
        && left.Name == right.Name
        && left.Type == right.Type
        && left.IsDisabled == right.IsDisabled
        && left.IsDnssecSigned == right.IsDnssecSigned
        && left.DnssecAlgorithm == right.DnssecAlgorithm
        && left.DenialOfExistenceMode == right.DenialOfExistenceMode
        && left.Nsec3Iterations == right.Nsec3Iterations
        && left.Nsec3SaltHex == right.Nsec3SaltHex
        && left.ValidateDnssec == right.ValidateDnssec
        && left.TsigKeyName == right.TsigKeyName
        && left.DynamicUpdateEnabled == right.DynamicUpdateEnabled
        && left.CloudProviderCredentialId == right.CloudProviderCredentialId
        && left.CloudProviderZoneId == right.CloudProviderZoneId
        && left.CloudSyncInterval == right.CloudSyncInterval
        && left.AutoManageCaaRecords == right.AutoManageCaaRecords
        && left.CaaIssuerDomain == right.CaaIssuerDomain
        && left.AutoManagePtrRecords == right.AutoManagePtrRecords
        && left.PtrIpv4PrefixLength == right.PtrIpv4PrefixLength
        && left.PtrIpv6PrefixLength == right.PtrIpv6PrefixLength
        && left.TransferTransport == right.TransferTransport
        && left.MemberOfCatalogZoneId == right.MemberOfCatalogZoneId
        && ListsMatch(left.PrimaryServerAddresses, right.PrimaryServerAddresses)
        && ListsMatch(left.ForwarderAddresses, right.ForwarderAddresses)
        && ListsMatch(left.NotifyAddresses, right.NotifyAddresses)
        && ListsMatch(left.UpdateTsigKeyNames, right.UpdateTsigKeyNames)
        && ListsMatch(left.TransferTsigKeyNames, right.TransferTsigKeyNames);

    /// <summary>
    ///     This method compares two record sets by id and content, whatever their order.
    /// </summary>
    private static bool RecordsMatch(
        IReadOnlyList<RecordEntity> cached,
        IReadOnlyList<RecordEntity> stored
    )
    {
        if (cached.Count != stored.Count)
            return false;

        // The database usually returns the rows in the order it returned them for the cached
        // copy, so walk both lists in step first. That costs no allocation. Only a difference
        // in order or content sends the walk to the slower comparison by id below.
        bool sameOrder = true;

        for (int index = 0; index < cached.Count; index++)
        {
            if (cached[index].Id != stored[index].Id)
            {
                sameOrder = false;
                break;
            }

            if (!RecordContentMatches(cached[index], stored[index]))
                return false;
        }

        if (sameOrder)
            return true;

        Dictionary<Guid, RecordEntity> cachedById = cached.ToDictionary(record => record.Id);

        foreach (RecordEntity record in stored)
        {
            if (
                !cachedById.TryGetValue(record.Id, out RecordEntity match)
                || !RecordContentMatches(match, record)
            )
                return false;
        }

        // Equal counts and every stored id found with equal content: the sets are equal, unless
        // the stored list repeats an id, which a primary key rules out.
        return true;
    }

    /// <summary>
    ///     This method compares the record fields that decide what the zone serves.
    /// </summary>
    private static bool RecordContentMatches(RecordEntity left, RecordEntity right) =>
        left.ZoneId == right.ZoneId
        && left.Name == right.Name
        && left.Type == right.Type
        && left.Class == right.Class
        && left.TimeToLiveSeconds == right.TimeToLiveSeconds
        && left.IsDisabled == right.IsDisabled
        && left.DataJson == right.DataJson;

    /// <summary>
    ///     This method compares two string lists in order. A <see langword="null" /> list equals an
    ///     empty one.
    /// </summary>
    private static bool ListsMatch(List<string> left, List<string> right) =>
        (left ?? []).SequenceEqual(right ?? []);
}
