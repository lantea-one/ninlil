using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class converts between the plain name/content/TTL(/priority) shape every one of the
///     three supported cloud providers' REST APIs models a simple record as, and Ninlil's
///     own <see cref="DnsResourceRecord" /> wire model -- shared by every <see cref="ICloudDnsProvider" />
///     implementation so each one's own HTTP/SDK client code stays focused on its provider's own
///     transport shape, not record-type mapping. <see cref="ToWireRecord" />/<see cref="FromWireRecord" />
///     cover the six record types every provider represents as one plain value (A, AAAA, CNAME,
///     MX, NS, TXT); SRV and CAA each carry several independent fields with no single "content"
///     value to genericize, so they instead get their own dedicated pairs --
///     <see cref="ToSrvWireRecord" />/<see cref="FromSrvWireRecord" /> and
///     <see cref="ToCaaWireRecord" />/<see cref="FromCaaWireRecord" />.
/// </summary>
public static class CloudDnsRecordMapper
{
    /// <summary>
    ///     This method builds a <see cref="DnsResourceRecord" /> from a provider's own plain
    ///     record fields.
    /// </summary>
    /// <param name="type">The record's type.</param>
    /// <param name="name">The record's owner name, in presentation form, with or without a trailing dot.</param>
    /// <param name="content">
    ///     The record's own single value: an address for A/AAAA, a target name for CNAME/NS/MX,
    ///     or free text for TXT.
    /// </param>
    /// <param name="timeToLiveSeconds">The record's time-to-live, in seconds.</param>
    /// <param name="mxPreference">An MX record's own preference value; ignored for every other type.</param>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="type" /> is not one of the six plain-content record types
    ///     this method covers -- SRV and CAA are both real <see cref="CloudDnsSyncSupportedRecordTypes.Types" />
    ///     members, but need <see cref="ToSrvWireRecord" />/<see cref="ToCaaWireRecord" /> instead.
    /// </exception>
    public static DnsResourceRecord ToWireRecord(
        DnsResourceRecordTypeEnum type,
        string name,
        string content,
        uint timeToLiveSeconds,
        ushort mxPreference = 0
    )
    {
        DnsName ownerName = DnsName.Parse(name);

        IDnsRecordData data = type switch
        {
            DnsResourceRecordTypeEnum.A => new ARecordData(IPAddress.Parse(content)),
            DnsResourceRecordTypeEnum.Aaaa => new AaaaRecordData(IPAddress.Parse(content)),
            DnsResourceRecordTypeEnum.Cname => new CnameRecordData(DnsName.Parse(content)),
            DnsResourceRecordTypeEnum.NS => new NsRecordData(DnsName.Parse(content)),
            DnsResourceRecordTypeEnum.MX => new MxRecordData(mxPreference, DnsName.Parse(content)),
            DnsResourceRecordTypeEnum.Txt => new TxtRecordData([content]),
            _ => throw new NotSupportedException(
                $"Record type '{type}' is not one of the cloud-DNS-sync-supported record types."
            ),
        };

        byte[] rdata = DnsRecordDataCodecRegistry.Encode(type, data);

        return new DnsResourceRecord(
            ownerName,
            type,
            DnsClassEnum.Internet,
            timeToLiveSeconds,
            rdata
        );
    }

    /// <summary>
    ///     This method reads a <see cref="DnsResourceRecord" /> back out into the plain
    ///     content/priority fields <see cref="ToWireRecord" /> built it from.
    /// </summary>
    /// <returns>The record's own single value, and an MX record's preference (0 for every other type).</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="record" />'s own type is not one of the six plain-content
    ///     record types this method covers (SRV/CAA need <see cref="FromSrvWireRecord" />/
    ///     <see cref="FromCaaWireRecord" /> instead), or its RDATA could not be decoded at all.
    /// </exception>
    public static (string Content, ushort MxPreference) FromWireRecord(DnsResourceRecord record)
    {
        IDnsRecordData data =
            DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
            ?? throw new NotSupportedException(
                $"No codec is registered for record type '{record.Type}'."
            );

        return data switch
        {
            ARecordData a => (a.Address.ToString(), (ushort)0),
            AaaaRecordData aaaa => (aaaa.Address.ToString(), (ushort)0),
            CnameRecordData cname => (TrimTrailingDot(cname.Target), (ushort)0),
            NsRecordData ns => (TrimTrailingDot(ns.NameServer), (ushort)0),
            MxRecordData mx => (TrimTrailingDot(mx.Exchange), mx.Preference),
            TxtRecordData txt => (string.Concat(txt.Values), (ushort)0),
            _ => throw new NotSupportedException(
                $"Record type '{record.Type}' is not one of the plain-content cloud-DNS-sync-supported record types."
            ),
        };
    }

    /// <summary>
    ///     This method builds a <see cref="DnsResourceRecordTypeEnum.Srv" /> <see cref="DnsResourceRecord" />
    ///     (RFC 2782) from a provider's own structured SRV fields -- SRV has no single plain-content
    ///     value the way <see cref="ToWireRecord" /> models the other supported types (its priority,
    ///     weight, port, and target are all independent RDATA fields), so it gets its own dedicated
    ///     pair alongside <see cref="FromSrvWireRecord" /> rather than sharing that generic path.
    /// </summary>
    /// <param name="name">The record's owner name, in presentation form, with or without a trailing dot.</param>
    /// <param name="priority">
    ///     A lower value is preferred over a higher one when more than one SRV record exists for
    ///     the same service.
    /// </param>
    /// <param name="weight">
    ///     Among records sharing the same <paramref name="priority" />, a client should prefer one
    ///     at random, weighted proportionally to this value.
    /// </param>
    /// <param name="port">The TCP or UDP port the service listens on.</param>
    /// <param name="target">The host providing the service, in presentation form.</param>
    /// <param name="timeToLiveSeconds">The record's time-to-live, in seconds.</param>
    public static DnsResourceRecord ToSrvWireRecord(
        string name,
        ushort priority,
        ushort weight,
        ushort port,
        string target,
        uint timeToLiveSeconds
    )
    {
        DnsName ownerName = DnsName.Parse(name);
        SrvRecordData data = new(priority, weight, port, DnsName.Parse(target));
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Srv, data);

        return new DnsResourceRecord(
            ownerName,
            DnsResourceRecordTypeEnum.Srv,
            DnsClassEnum.Internet,
            timeToLiveSeconds,
            rdata
        );
    }

    /// <summary>
    ///     This method reads a <see cref="DnsResourceRecordTypeEnum.Srv" /> <see cref="DnsResourceRecord" />
    ///     back out into the structured fields <see cref="ToSrvWireRecord" /> built it from.
    /// </summary>
    /// <param name="record">The record to read; must be of type <see cref="DnsResourceRecordTypeEnum.Srv" />.</param>
    /// <returns>The record's own priority, weight, port, and target (without a trailing dot).</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="record" />'s own RDATA could not be decoded at all.
    /// </exception>
    public static (ushort Priority, ushort Weight, ushort Port, string Target) FromSrvWireRecord(
        DnsResourceRecord record
    )
    {
        SrvRecordData data =
            (SrvRecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
            ?? throw new NotSupportedException(
                $"No codec is registered for record type '{record.Type}'."
            );

        return (data.Priority, data.Weight, data.Port, TrimTrailingDot(data.Target));
    }

    /// <summary>
    ///     This method builds a <see cref="DnsResourceRecordTypeEnum.Caa" /> <see cref="DnsResourceRecord" />
    ///     (RFC 8659) from a provider's own structured CAA fields -- see <see cref="ToSrvWireRecord" />'s
    ///     own doc comment for why CAA needs its own dedicated pair rather than sharing
    ///     <see cref="ToWireRecord" />'s generic single-content-value path.
    /// </summary>
    /// <param name="name">The record's owner name, in presentation form, with or without a trailing dot.</param>
    /// <param name="isCritical">
    ///     Whether a certificate authority that does not recognize <paramref name="tag" /> must
    ///     refuse to issue a certificate, rather than ignoring this record.
    /// </param>
    /// <param name="tag">
    ///     What kind of policy this record expresses -- commonly <c>issue</c>, <c>issuewild</c>,
    ///     or <c>iodef</c>.
    /// </param>
    /// <param name="value">The policy value itself -- for an <c>issue</c> tag, the permitted issuer's domain name.</param>
    /// <param name="timeToLiveSeconds">The record's time-to-live, in seconds.</param>
    public static DnsResourceRecord ToCaaWireRecord(
        string name,
        bool isCritical,
        string tag,
        string value,
        uint timeToLiveSeconds
    )
    {
        DnsName ownerName = DnsName.Parse(name);
        CaaRecordData data = new(isCritical, tag, value);
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Caa, data);

        return new DnsResourceRecord(
            ownerName,
            DnsResourceRecordTypeEnum.Caa,
            DnsClassEnum.Internet,
            timeToLiveSeconds,
            rdata
        );
    }

    /// <summary>
    ///     This method reads a <see cref="DnsResourceRecordTypeEnum.Caa" /> <see cref="DnsResourceRecord" />
    ///     back out into the structured fields <see cref="ToCaaWireRecord" /> built it from.
    /// </summary>
    /// <param name="record">The record to read; must be of type <see cref="DnsResourceRecordTypeEnum.Caa" />.</param>
    /// <returns>The record's own criticality flag, tag, and value.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="record" />'s own RDATA could not be decoded at all.
    /// </exception>
    public static (bool IsCritical, string Tag, string Value) FromCaaWireRecord(
        DnsResourceRecord record
    )
    {
        CaaRecordData data =
            (CaaRecordData)DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
            ?? throw new NotSupportedException(
                $"No codec is registered for record type '{record.Type}'."
            );

        return (data.IsCritical, data.Tag, data.Value);
    }

    /// <summary>
    ///     This method converts a <see cref="DnsName" /> to presentation form without its own
    ///     always-present trailing dot, matching how every provider's REST API expects a target
    ///     name to look, and how <c>RecordEntity.Name</c> itself is stored.
    /// </summary>
    private static string TrimTrailingDot(DnsName name) => name.ToString().TrimEnd('.');
}
