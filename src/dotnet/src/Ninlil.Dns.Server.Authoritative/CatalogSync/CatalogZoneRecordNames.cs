using System.Security.Cryptography;
using System.Text;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class builds and parses the owner names RFC 9432 (DNS Catalog Zones) defines: the
///     <c>version</c> TXT record and each member's PTR record under <c>zones</c>. All names are
///     relative to a catalog zone's apex (<c>$CATZ</c> in RFC 9432). Only these are supported, not
///     the <c>group</c> or <c>coo</c> properties.
/// </summary>
public static class CatalogZoneRecordNames
{
    /// <summary>
    ///     This constant contains the catalog schema version produced and accepted, the RFC 9432 §3
    ///     <c>version</c> TXT value. RFC 9432 is schema version 2; version 1 is not supported.
    /// </summary>
    public const string SchemaVersion = "2";

    /// <summary>
    ///     This method builds the owner name of the version TXT record, <c>version.$CATZ</c>
    ///     (RFC 9432 §3).
    /// </summary>
    /// <param name="catalogZoneName">The catalog zone's fully-qualified name, without a trailing dot.</param>
    public static string VersionRecordOwnerName(string catalogZoneName) =>
        $"version.{catalogZoneName.TrimEnd('.')}";

    /// <summary>
    ///     This method builds the name member entries live under, <c>zones.$CATZ</c> (RFC 9432 §5).
    /// </summary>
    /// <param name="catalogZoneName">The catalog zone's fully-qualified name, without a trailing dot.</param>
    public static string MembersContainerName(string catalogZoneName) =>
        $"zones.{catalogZoneName.TrimEnd('.')}";

    /// <summary>
    ///     This method computes a member's unique label (<c>&lt;unique-N&gt;</c>, RFC 9432 §5). RFC 9432
    ///     leaves the algorithm open; this uses the lowercase hex SHA-1 of the member's lowercase name
    ///     without a trailing dot. Deriving it from the name means no mapping is stored: a rename
    ///     gives a new label, and consumers see a remove and an add.
    /// </summary>
    /// <param name="memberZoneName">The member zone's fully-qualified name, without a trailing dot.</param>
    public static string ComputeUniqueLabel(string memberZoneName)
    {
        byte[] digest = SHA1.HashData(
            Encoding.ASCII.GetBytes(memberZoneName.TrimEnd('.').ToLowerInvariant())
        );
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    ///     This method builds a member's PTR owner name, <c>&lt;unique-N&gt;.zones.$CATZ</c>
    ///     (RFC 9432 §5).
    /// </summary>
    /// <param name="catalogZoneName">The catalog zone's fully-qualified name, without a trailing dot.</param>
    /// <param name="uniqueLabel">The member's unique label, from <see cref="ComputeUniqueLabel" />.</param>
    public static string MemberRecordOwnerName(string catalogZoneName, string uniqueLabel) =>
        $"{uniqueLabel}.{MembersContainerName(catalogZoneName)}";

    /// <summary>
    ///     This method checks whether a stored record is a member entry (a PTR exactly one label under
    ///     <see cref="MembersContainerName" />, not a two-label <c>group</c> or <c>coo</c> entry) and
    ///     decodes the member zone name.
    /// </summary>
    /// <param name="record">The stored record to check.</param>
    /// <param name="catalogZoneName">The catalog zone's fully-qualified name, without a trailing dot.</param>
    /// <param name="memberZoneName">
    ///     The member zone's name, without a trailing dot, if <paramref name="record" /> is a member
    ///     entry; otherwise <see langword="null" />.
    /// </param>
    /// <returns>Whether the record is a member entry.</returns>
    public static bool TryGetMemberZoneName(
        RecordEntity record,
        string catalogZoneName,
        out string memberZoneName
    )
    {
        memberZoneName = null;

        if (record.Type != DnsResourceRecordTypeEnum.Ptr)
            return false;

        if (!DnsName.TryParse(record.Name, out DnsName ownerName))
            return false;

        DnsName container = DnsName.Parse(MembersContainerName(catalogZoneName));

        // A member entry has one label (<unique-N>) before "zones.$CATZ"; "group." and "coo."
        // entries have two.
        if (ownerName.Labels.Count != container.Labels.Count + 1)
            return false;

        DnsName ownerSuffix = new([.. ownerName.Labels.Skip(1)]);
        if (ownerSuffix != container)
            return false;

        if (RecordEntityCodec.DecodeTypedData(record) is not PtrRecordData ptrData)
            return false;

        memberZoneName = ptrData.Target.ToString().TrimEnd('.');
        return true;
    }
}
