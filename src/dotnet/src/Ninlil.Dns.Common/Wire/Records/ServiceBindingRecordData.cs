using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents one connection parameter inside a
///     <see cref="ServiceBindingRecordData" /> record, such as the parameter that lists which
///     Application-Layer Protocol Negotiation values (<c>h2</c>, <c>h3</c>) a service supports.
///     <para>
///         Like <see cref="DnsResourceRecord" />, this is a plain class rather than a record:
///         <see cref="Value" /> is a <see langword="byte[]" />, and compiler-generated equality
///         would compare it by reference instead of by content.
///     </para>
/// </summary>
/// <param name="key">
///     The parameter's numeric key, assigned by IANA in the "DNS Service Binding (SVCB) Parameter
///     Registry"; for example 1 is <c>alpn</c> and 3 is <c>port</c>.
/// </param>
/// <param name="value">The parameter's value, in the format its key defines.</param>
public class SvcbParameter(ushort key, byte[] value) : IEquatable<SvcbParameter>
{
    /// <summary>
    ///     This property contains the parameter's numeric key.
    /// </summary>
    public ushort Key { get; } = key;

    /// <summary>
    ///     This property contains the parameter's value.
    /// </summary>
    public byte[] Value { get; } = value;

    /// <inheritdoc />
    public bool Equals(SvcbParameter other) =>
        other is not null
        && (
            ReferenceEquals(this, other)
            || (Key == other.Key && Value.AsSpan().SequenceEqual(other.Value))
        );

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as SvcbParameter);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Key);

        foreach (byte b in Value)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class represents the data shared by <see cref="DnsResourceRecordTypeEnum.Svcb" /> and
///     <see cref="DnsResourceRecordTypeEnum.Https" /> records, both defined by RFC 9460. They tell
///     a client how to reach a service at the owner name (an alternate port, supported protocol
///     versions, and so on) before it opens a connection.
/// </summary>
/// <param name="Priority">
///     0 marks an "alias form" record that points at <see cref="TargetName" /> and has no
///     parameters. Any other value is this record's preference among records at the same name,
///     lower being preferred.
/// </param>
/// <param name="TargetName">
///     The name that provides the service. In service form, the root name means the owner name
///     itself.
/// </param>
/// <param name="Parameters">The connection parameters this record advertises.</param>
public record ServiceBindingRecordData(
    ushort Priority,
    DnsName TargetName,
    IReadOnlyList<SvcbParameter> Parameters
) : IDnsRecordData;

/// <summary>
///     This class contains the wire encoding <see cref="SvcbRecordDataCodec" /> and
///     <see cref="HttpsRecordDataCodec" /> share. RFC 9460 gives both record types the same
///     layout; they differ only in what a client uses them for.
/// </summary>
internal static class ServiceBindingCodec
{
    /// <summary>
    ///     This method decodes an SVCB-family record's RDATA.
    /// </summary>
    public static ServiceBindingRecordData Read(ReadOnlySpan<byte> rdata)
    {
        ushort priority = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);

        // RFC 9460 §2.2 requires the target name to be uncompressed.
        int offset = 2;
        DnsName targetName = UncompressedDnsNameCodec.Read(rdata, ref offset);

        List<SvcbParameter> parameters = [];

        // The rest of the RDATA is a run of parameters (RFC 9460 §2.2): a 2-octet key, a
        // 2-octet value length, then that many octets of value, until the RDATA ends.
        while (offset < rdata.Length)
        {
            ushort key = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(offset, 2));
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(offset + 2, 2));
            byte[] value = rdata.Slice(offset + 4, length).ToArray();

            parameters.Add(new SvcbParameter(key, value));
            offset += 4 + length;
        }

        return new ServiceBindingRecordData(priority, targetName, parameters);
    }

    /// <summary>
    ///     This method decodes an SVCB-family record's RDATA, resolving a compression pointer in the
    ///     target name against the full message. RFC 9460 §2.2 forbids such a pointer; this reads it
    ///     anyway to be lenient with nonconforming senders.
    /// </summary>
    public static ServiceBindingRecordData Read(
        ReadOnlySpan<byte> rdata,
        DecompressingNameReader decompressor
    )
    {
        ushort priority = BinaryPrimitives.ReadUInt16BigEndian(rdata[..2]);
        int offset = 2;
        DnsName targetName = decompressor.Read(ref offset);

        List<SvcbParameter> parameters = [];

        // The rest of the RDATA is a run of parameters (RFC 9460 §2.2): a 2-octet key, a
        // 2-octet value length, then that many octets of value, until the RDATA ends.
        while (offset < rdata.Length)
        {
            ushort key = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(offset, 2));
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(rdata.Slice(offset + 2, 2));
            byte[] value = rdata.Slice(offset + 4, length).ToArray();

            parameters.Add(new SvcbParameter(key, value));
            offset += 4 + length;
        }

        return new ServiceBindingRecordData(priority, targetName, parameters);
    }

    /// <summary>
    ///     This method encodes an SVCB-family record's RDATA.
    /// </summary>
    public static byte[] Write(ServiceBindingRecordData data)
    {
        ArrayBufferWriter<byte> buffer = new();

        Span<byte> prioritySpan = buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(prioritySpan, data.Priority);
        buffer.Advance(2);

        UncompressedDnsNameCodec.Write(buffer, data.TargetName);

        foreach (SvcbParameter parameter in data.Parameters)
        {
            Span<byte> keySpan = buffer.GetSpan(2);
            BinaryPrimitives.WriteUInt16BigEndian(keySpan, parameter.Key);
            buffer.Advance(2);

            Span<byte> lengthSpan = buffer.GetSpan(2);
            BinaryPrimitives.WriteUInt16BigEndian(lengthSpan, (ushort)parameter.Value.Length);
            buffer.Advance(2);

            parameter.Value.CopyTo(buffer.GetSpan(parameter.Value.Length));
            buffer.Advance(parameter.Value.Length);
        }

        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>
///     This class reads and writes <see cref="ServiceBindingRecordData" /> for the
///     <see cref="DnsResourceRecordTypeEnum.Svcb" /> record type.
/// </summary>
public class SvcbRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Svcb;

    /// <inheritdoc />
    public Type DataType => typeof(ServiceBindingRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) => ServiceBindingCodec.Read(rdata);

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor) =>
        ServiceBindingCodec.Read(rdata, decompressor);

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data) =>
        ServiceBindingCodec.Write((ServiceBindingRecordData)data);
}

/// <summary>
///     This class reads and writes <see cref="ServiceBindingRecordData" /> for the
///     <see cref="DnsResourceRecordTypeEnum.Https" /> record type.
/// </summary>
public class HttpsRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.Https;

    /// <inheritdoc />
    public Type DataType => typeof(ServiceBindingRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) => ServiceBindingCodec.Read(rdata);

    /// <inheritdoc />
    public bool CanEmbedCompressedName => true;

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor) =>
        ServiceBindingCodec.Read(rdata, decompressor);

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data) =>
        ServiceBindingCodec.Write((ServiceBindingRecordData)data);
}
