using System.Buffers;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This class represents the data of an <see cref="DnsResourceRecordTypeEnum.OpenPgpKey" />
///     record, defined by RFC 7929: an OpenPGP public key published for the owner name, which
///     itself encodes the local part of an e-mail address's SHA-256 hash rather than the mailbox
///     directly. RDATA is the raw OpenPGP Transferable Public Key packet sequence (RFC 4880)
///     with no framing of its own -- there is nothing else in the record to read.
///     <para>
///         This is a plain class rather than a record, since <see cref="KeyData" /> is a
///         <see langword="byte[]" /> and a compiler-generated equality would compare it by
///         reference rather than by content.
///     </para>
/// </summary>
/// <param name="keyData">The raw OpenPGP Transferable Public Key packet sequence.</param>
public class OpenPgpKeyRecordData(byte[] keyData) : IDnsRecordData, IEquatable<OpenPgpKeyRecordData>
{
    /// <summary>
    ///     This property contains the raw OpenPGP Transferable Public Key packet sequence.
    /// </summary>
    public byte[] KeyData { get; } = keyData;

    /// <inheritdoc />
    public bool Equals(OpenPgpKeyRecordData other) =>
        other is not null
        && (ReferenceEquals(this, other) || KeyData.AsSpan().SequenceEqual(other.KeyData));

    /// <inheritdoc />
    public override bool Equals(object obj) => Equals(obj as OpenPgpKeyRecordData);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hash = new();

        foreach (byte b in KeyData)
            hash.Add(b);

        return hash.ToHashCode();
    }
}

/// <summary>
///     This class reads and writes <see cref="OpenPgpKeyRecordData" />.
/// </summary>
public class OpenPgpKeyRecordDataCodec : IDnsRecordDataCodec
{
    /// <inheritdoc />
    public DnsResourceRecordTypeEnum RecordType => DnsResourceRecordTypeEnum.OpenPgpKey;

    /// <inheritdoc />
    public Type DataType => typeof(OpenPgpKeyRecordData);

    /// <inheritdoc />
    public IDnsRecordData Read(ReadOnlySpan<byte> rdata) =>
        new OpenPgpKeyRecordData(rdata.ToArray());

    /// <inheritdoc />
    public byte[] Write(IDnsRecordData data)
    {
        OpenPgpKeyRecordData typed = (OpenPgpKeyRecordData)data;
        ArrayBufferWriter<byte> buffer = new();

        typed.KeyData.CopyTo(buffer.GetSpan(typed.KeyData.Length));
        buffer.Advance(typed.KeyData.Length);

        return buffer.WrittenSpan.ToArray();
    }
}
