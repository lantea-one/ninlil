using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class finds the zone hosted on this server that publishes the DS record of a child
///     zone. <see cref="DnssecKeyRolloverBackgroundService" /> uses it to decide whether a KSK
///     rollover can cut over at once, and <see cref="SignedZoneCache" /> uses it to re-sign that
///     parent when the child's DS-relevant keys change, so both agree on what a local parent is.
/// </summary>
internal static class LocalParentLocator
{
    /// <summary>
    ///     This field contains the TTL <see cref="ZoneSigningService" /> gives generated DS
    ///     records, the floor of the DS TTL when the parent is hosted here (<see cref="Find" />
    ///     raises it to a longer stored DS TTL). The two values must stay equal.
    /// </summary>
    internal static readonly TimeSpan LocalDelegationDsTtl = TimeSpan.FromSeconds(3600);

    /// <summary>
    ///     This method finds the local parent of a child zone: the zone this server answers the
    ///     child's DS query from (<see cref="AuthoritativeZoneCache.FindZone" /> with type DS), at
    ///     any label distance above the child. It counts only when that zone is a signed Primary
    ///     or CloudPrimary and its topmost zone cut at the child's name is the child itself, so a
    ///     child hidden below a higher cut is external. Only then is the DS generated
    ///     automatically. It reads the in-memory cache, which every zone write reloads, so it does
    ///     not query the database.
    /// </summary>
    /// <param name="zoneCache">The cache of the zones hosted here.</param>
    /// <param name="childName">The name of the child zone.</param>
    /// <param name="childZoneId">The identifier of the child zone, which can never be its own parent.</param>
    /// <returns>
    ///     Whether the parent is hosted here, the parent's zone identifier (null when it is not),
    ///     and the DS TTL to wait for: at least <see cref="LocalDelegationDsTtl" />, or the
    ///     longest TTL of a DS record stored at the child's name in the parent when that is
    ///     longer, because the signer gives the DS RRset the stored TTL (RFC 4034 §3 needs the
    ///     RRSIG to carry the same TTL). For an external parent the TTL is the 3600-second
    ///     floor and the caller does not use it.
    /// </returns>
    internal static (bool IsLocalParent, Guid? ParentZoneId, TimeSpan DsTtl) Find(
        AuthoritativeZoneCache zoneCache,
        DnsName childName,
        Guid childZoneId
    )
    {
        CachedZone parent = zoneCache.FindZone(childName, DnsResourceRecordTypeEnum.DS);

        if (
            parent is null
            || parent.Zone.Id == childZoneId
            || !parent.Zone.IsDnssecSigned
            || parent.Zone.Type is not (ZoneTypeEnum.Primary or ZoneTypeEnum.CloudPrimary)
        )
            return (false, null, LocalDelegationDsTtl);

        // Names below a cut are not authoritative data of the zone above it (RFC 1034 §4.2.1),
        // so a cut above the child leaves the parent without a delegation for the child itself
        // and publishes no DS for it.
        if (parent.FindEnclosingDelegation(childName)?.DelegationPoint != childName)
            return (false, null, LocalDelegationDsTtl);

        uint storedTtlSeconds = parent
            .GetRecords(childName, DnsResourceRecordTypeEnum.DS)
            .Select(record => record.TimeToLiveSeconds)
            .DefaultIfEmpty(0u)
            .Max();

        return (
            true,
            parent.Zone.Id,
            TimeSpan.FromSeconds(Math.Max(LocalDelegationDsTtl.TotalSeconds, storedTtlSeconds))
        );
    }
}
