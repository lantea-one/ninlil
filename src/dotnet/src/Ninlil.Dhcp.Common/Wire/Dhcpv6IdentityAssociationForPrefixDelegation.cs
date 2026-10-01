using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.21's Identity Association for Prefix Delegation
///     (IA_PD) option, the prefix counterpart of
///     <see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses" />: a container named by a
///     client-chosen IAID, with T1/T2 timers and nested options, holding
///     <see cref="Dhcpv6IdentityAssociationPrefix" /> options instead of addresses.
/// </summary>
public class Dhcpv6IdentityAssociationForPrefixDelegation
{
    /// <summary>
    ///     This constant contains the length of the fixed part (the IAID and the 4-byte T1 and T2
    ///     fields) before the nested options.
    /// </summary>
    private const int FixedLength = 12;

    /// <summary>
    ///     This property contains the client-chosen Identity Association Identifier (IAID), unique
    ///     among the client's associations of this type.
    /// </summary>
    public uint IdentityAssociationId { get; set; }

    /// <summary>
    ///     This property contains how long after this option is sent the client should wait
    ///     before asking the delegating server to renew the prefixes. This is RFC 8415 Section
    ///     21.21's "T1" field, in whole seconds on the wire.
    /// </summary>
    public TimeSpan T1 { get; set; }

    /// <summary>
    ///     This property contains how long after this option is sent the client should wait
    ///     before asking any server to extend the prefixes. This is RFC 8415 Section 21.21's "T2"
    ///     field, in whole seconds on the wire.
    /// </summary>
    public TimeSpan T2 { get; set; }

    /// <summary>
    ///     This property contains the prefixes delegated to (or requested for) this association,
    ///     decoded from its nested IA Prefix options.
    /// </summary>
    public List<Dhcpv6IdentityAssociationPrefix> Prefixes { get; } = [];

    /// <summary>
    ///     This property contains the other nested options (usually a Status Code option).
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IA_PD option's value (everything after the option header).
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        out Dhcpv6IdentityAssociationForPrefixDelegation association,
        out string failureReason
    )
    {
        association = null;

        if (data.Length < FixedLength)
        {
            failureReason = "An IA_PD option is shorter than its own fixed 12-byte portion.";
            return false;
        }

        Dhcpv6IdentityAssociationForPrefixDelegation result = new()
        {
            IdentityAssociationId = BinaryPrimitives.ReadUInt32BigEndian(data[..4]),
            T1 = TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4))),
            T2 = TimeSpan.FromSeconds(BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4))),
        };

        List<Dhcpv6Option> nestedOptions = [];

        if (
            !Dhcpv6MessageReader.TryReadOptions(
                data[FixedLength..],
                nestedOptions,
                out failureReason
            )
        )
            return false;

        foreach (Dhcpv6Option option in nestedOptions)
        {
            if (option.Code == (ushort)Dhcpv6OptionCodeEnum.IaPrefix)
            {
                if (
                    !Dhcpv6IdentityAssociationPrefix.TryDecode(
                        option.Data,
                        out Dhcpv6IdentityAssociationPrefix prefix,
                        out failureReason
                    )
                )
                    return false;

                result.Prefixes.Add(prefix);
            }
            else
            {
                result.Options.Add(option);
            }
        }

        association = result;
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method encodes this option's value (everything after the option header, which
    ///     <see cref="Dhcpv6MessageOptionExtensions" /> adds). <see cref="Prefixes" /> are written
    ///     before <see cref="Options" />.
    /// </summary>
    internal byte[] Encode()
    {
        ArrayBufferWriter<byte> buffer = new(FixedLength);

        Span<byte> fixedFields = buffer.GetSpan(FixedLength);
        BinaryPrimitives.WriteUInt32BigEndian(fixedFields, IdentityAssociationId);
        BinaryPrimitives.WriteUInt32BigEndian(
            fixedFields[4..],
            Dhcpv6IdentityAssociationAddress.ToWireSeconds(T1)
        );
        BinaryPrimitives.WriteUInt32BigEndian(
            fixedFields[8..],
            Dhcpv6IdentityAssociationAddress.ToWireSeconds(T2)
        );
        buffer.Advance(FixedLength);

        foreach (Dhcpv6IdentityAssociationPrefix prefix in Prefixes)
        {
            byte[] encodedPrefix = prefix.Encode();
            Span<byte> optionHeader = buffer.GetSpan(Dhcpv6Constants.OptionHeaderLength);
            BinaryPrimitives.WriteUInt16BigEndian(
                optionHeader,
                (ushort)Dhcpv6OptionCodeEnum.IaPrefix
            );
            BinaryPrimitives.WriteUInt16BigEndian(optionHeader[2..], (ushort)encodedPrefix.Length);
            buffer.Advance(Dhcpv6Constants.OptionHeaderLength);

            encodedPrefix.CopyTo(buffer.GetSpan(encodedPrefix.Length));
            buffer.Advance(encodedPrefix.Length);
        }

        Dhcpv6MessageWriter.WriteOptions(buffer, Options, nameof(Options));

        return buffer.WrittenSpan.ToArray();
    }
}
