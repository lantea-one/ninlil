using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class converts between the name, content, TTL and priority fields the providers' APIs
///     use and Ninlil's <see cref="DnsResourceRecord" />, for every <see cref="ICloudDnsProvider" />.
///     <see cref="ToWireRecord" /> and <see cref="FromWireRecord" /> cover the single-value types
///     (A, AAAA, CNAME, MX, NS, TXT); SRV and CAA have several fields and their methods.
/// </summary>
public static class CloudDnsRecordMapper
{
    /// <summary>
    ///     This method builds a <see cref="DnsResourceRecord" /> from a provider's plain record
    ///     fields.
    /// </summary>
    /// <param name="type">The record's type.</param>
    /// <param name="name">The record's owner name, in presentation form, with or without a trailing dot.</param>
    /// <param name="content">
    ///     The record's value: an address for A/AAAA, a target name for CNAME/NS/MX, or text for
    ///     TXT.
    /// </param>
    /// <param name="timeToLiveSeconds">The record's time-to-live, in seconds.</param>
    /// <param name="mxPreference">The MX preference; ignored for other types.</param>
    /// <exception cref="NotSupportedException">
    ///     Thrown when <paramref name="type" /> is not one of the six single-value types; SRV and CAA
    ///     use <see cref="ToSrvWireRecord" /> and <see cref="ToCaaWireRecord" />.
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
    /// <returns>The record's value, and the MX preference (0 for other types).</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the record is not one of the six single-value types, or its RDATA cannot be
    ///     decoded.
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
    ///     This method builds an SRV <see cref="DnsResourceRecord" /> (RFC 2782) from a provider's
    ///     priority, weight, port and target fields.
    /// </summary>
    /// <param name="name">The record's owner name, in presentation form, with or without a trailing dot.</param>
    /// <param name="priority">
    ///     The priority; lower values are preferred.
    /// </param>
    /// <param name="weight">
    ///     The weight for choosing among records of equal priority.
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
    ///     This method reads an SRV <see cref="DnsResourceRecord" /> into the fields
    ///     <see cref="ToSrvWireRecord" /> takes.
    /// </summary>
    /// <param name="record">The record to read; must be of type <see cref="DnsResourceRecordTypeEnum.Srv" />.</param>
    /// <returns>The priority, weight, port and target (without a trailing dot).</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the record's RDATA cannot be decoded.
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
    ///     This method builds a CAA <see cref="DnsResourceRecord" /> (RFC 8659) from a provider's flag,
    ///     tag and value fields.
    /// </summary>
    /// <param name="name">The record's owner name, in presentation form, with or without a trailing dot.</param>
    /// <param name="isCritical">
    ///     Whether a CA that does not understand <paramref name="tag" /> must refuse to issue.
    /// </param>
    /// <param name="tag">
    ///     The property tag, usually <c>issue</c>, <c>issuewild</c> or <c>iodef</c>.
    /// </param>
    /// <param name="value">The property value; for <c>issue</c>, the permitted CA's domain.</param>
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
    ///     This method reads a CAA <see cref="DnsResourceRecord" /> into the fields
    ///     <see cref="ToCaaWireRecord" /> takes.
    /// </summary>
    /// <param name="record">The record to read; must be of type <see cref="DnsResourceRecordTypeEnum.Caa" />.</param>
    /// <returns>The critical flag, tag and value.</returns>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the record's RDATA cannot be decoded.
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
    ///     This method converts a <see cref="DnsName" /> to presentation form without the trailing dot,
    ///     as the providers expect and as <c>RecordEntity.Name</c> is stored.
    /// </summary>
    private static string TrimTrailingDot(DnsName name) => name.ToString().TrimEnd('.');
}
