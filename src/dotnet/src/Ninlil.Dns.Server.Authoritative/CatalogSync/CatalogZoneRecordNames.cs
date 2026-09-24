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
///     This class builds and parses the well-known record owner names RFC 9432 (DNS Catalog
///     Zones) defines: the catalog's own schema-version TXT record, and each member zone's own
///     PTR record under the catalog's <c>zones</c> container. Every name this class produces or
///     recognizes is relative to a specific catalog zone's own apex name (RFC 9432's own
///     <c>$CATZ</c> placeholder).
/// </summary>
public static class CatalogZoneRecordNames
{
    /// <summary>
    ///     This constant contains the schema version this project's own catalog zone producer and
    ///     consumer both speak -- RFC 9432 §3's own "version" TXT record value. RFC 9432 is
    ///     itself version 2 of the catalog zone schema (superseding the version-1 shape an earlier
    ///     Internet-Draft revision described); this project implements only version 2.
    /// </summary>
    public const string SchemaVersion = "2";

    /// <summary>
    ///     This method builds the owner name of a catalog zone's own schema-version TXT record --
    ///     RFC 9432 §3's <c>version.$CATZ</c>.
    /// </summary>
    /// <param name="catalogZoneName">The catalog zone's own fully-qualified name, without a trailing dot.</param>
    public static string VersionRecordOwnerName(string catalogZoneName) =>
        $"version.{catalogZoneName.TrimEnd('.')}";

    /// <summary>
    ///     This method builds the owner-name suffix every member entry lives under -- RFC 9432
    ///     §5's <c>zones.$CATZ</c> container.
    /// </summary>
    /// <param name="catalogZoneName">The catalog zone's own fully-qualified name, without a trailing dot.</param>
    public static string MembersContainerName(string catalogZoneName) =>
        $"zones.{catalogZoneName.TrimEnd('.')}";

    /// <summary>
    ///     This method computes the unique label (RFC 9432 §5's <c>&lt;unique-N&gt;</c>) this
    ///     project's own catalog producer assigns a member zone. RFC 9432 leaves the exact
    ///     algorithm unspecified -- only that it "has a unique value" within the catalog -- so
    ///     this project derives it deterministically as the lowercase hexadecimal SHA-1 digest of
    ///     the member zone's own lowercase, trailing-dot-free name. Deriving it from the name
    ///     itself, rather than a randomly generated and separately stored value, means a rename
    ///     naturally produces a new label (the old one simply stops being emitted, and a new one
    ///     appears) instead of needing its own persisted mapping -- a consumer's ordinary
    ///     add/remove diff already handles that correctly on its own.
    /// </summary>
    /// <param name="memberZoneName">The member zone's own fully-qualified name, without a trailing dot.</param>
    public static string ComputeUniqueLabel(string memberZoneName)
    {
        byte[] digest = SHA1.HashData(
            Encoding.ASCII.GetBytes(memberZoneName.TrimEnd('.').ToLowerInvariant())
        );
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    ///     This method builds a single member's own PTR record owner name -- RFC 9432 §5's
    ///     <c>&lt;unique-N&gt;.zones.$CATZ</c>.
    /// </summary>
    /// <param name="catalogZoneName">The catalog zone's own fully-qualified name, without a trailing dot.</param>
    /// <param name="uniqueLabel">The member's own unique label, from <see cref="ComputeUniqueLabel" />.</param>
    public static string MemberRecordOwnerName(string catalogZoneName, string uniqueLabel) =>
        $"{uniqueLabel}.{MembersContainerName(catalogZoneName)}";

    /// <summary>
    ///     This method determines whether a single stored record is one catalog member's own PTR
    ///     entry -- a PTR record whose owner name is exactly one label under
    ///     <see cref="MembersContainerName" /> (as opposed to, for example, a <c>group.</c> or
    ///     <c>coo.</c> two-label sub-entry RFC 9432 also defines under the same container, neither
    ///     of which this project implements -- see this class's own remarks on scope), and
    ///     decodes the member zone name it names.
    /// </summary>
    /// <param name="record">The stored record to check.</param>
    /// <param name="catalogZoneName">The catalog zone's own fully-qualified name, without a trailing dot.</param>
    /// <param name="memberZoneName">
    ///     The member zone's own name (without a trailing dot) if <paramref name="record" /> is a
    ///     genuine member entry, otherwise <see langword="null" />.
    /// </param>
    /// <returns><see langword="true" /> if this is a genuine member entry, <see langword="false" /> otherwise.</returns>
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

        // A genuine member entry has exactly one label -- <unique-N> -- ahead of "zones.$CATZ".
        // A "group."/"coo." sub-entry has two (its own qualifier label, then <unique-N>), so this
        // length check alone is what tells the two apart without needing to special-case either
        // qualifier by name.
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
