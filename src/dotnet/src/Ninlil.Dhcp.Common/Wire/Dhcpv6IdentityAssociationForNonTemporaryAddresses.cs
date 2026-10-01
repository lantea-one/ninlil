using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.4's Identity Association for Non-temporary
///     Addresses (IA_NA) option: a container named by a client-chosen IAID, with the T1/T2 timers
///     and nested options, usually one or more <see cref="Dhcpv6IdentityAssociationAddress" />
///     options and sometimes a Status Code for the whole association. A message can carry several
///     IA_NAs, so <see cref="Dhcpv6MessageOptionExtensions" /> exposes them as a list.
/// </summary>
public class Dhcpv6IdentityAssociationForNonTemporaryAddresses
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
    ///     before asking the assigning server to renew (a RENEW). This is RFC 8415 Section 21.4's
    ///     "T1" field, in whole seconds on the wire.
    /// </summary>
    public TimeSpan T1 { get; set; }

    /// <summary>
    ///     This property contains how long after this option is sent the client should wait
    ///     before asking any server to extend the addresses (a REBIND, after an unanswered RENEW).
    ///     This is RFC 8415 Section 21.4's "T2" field, in whole seconds on the wire.
    /// </summary>
    public TimeSpan T2 { get; set; }

    /// <summary>
    ///     This property contains the addresses assigned to (or requested for) this association,
    ///     decoded from its nested IA Address options.
    /// </summary>
    public List<Dhcpv6IdentityAssociationAddress> Addresses { get; } = [];

    /// <summary>
    ///     This property contains the other nested options (usually a Status Code option):
    ///     everything except IA Address options, which go to <see cref="Addresses" />.
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IA_NA option's value (everything after the option header).
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        out Dhcpv6IdentityAssociationForNonTemporaryAddresses association,
        out string failureReason
    )
    {
        association = null;

        if (data.Length < FixedLength)
        {
            failureReason = "An IA_NA option is shorter than its own fixed 12-byte portion.";
            return false;
        }

        Dhcpv6IdentityAssociationForNonTemporaryAddresses result = new()
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
            if (option.Code == (ushort)Dhcpv6OptionCodeEnum.IaAddress)
            {
                if (
                    !Dhcpv6IdentityAssociationAddress.TryDecode(
                        option.Data,
                        out Dhcpv6IdentityAssociationAddress address,
                        out failureReason
                    )
                )
                    return false;

                result.Addresses.Add(address);
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
    ///     <see cref="Dhcpv6MessageOptionExtensions" /> adds). <see cref="Addresses" /> are written
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

        foreach (Dhcpv6IdentityAssociationAddress address in Addresses)
        {
            byte[] encodedAddress = address.Encode();
            Span<byte> optionHeader = buffer.GetSpan(Dhcpv6Constants.OptionHeaderLength);
            BinaryPrimitives.WriteUInt16BigEndian(
                optionHeader,
                (ushort)Dhcpv6OptionCodeEnum.IaAddress
            );
            BinaryPrimitives.WriteUInt16BigEndian(optionHeader[2..], (ushort)encodedAddress.Length);
            buffer.Advance(Dhcpv6Constants.OptionHeaderLength);

            encodedAddress.CopyTo(buffer.GetSpan(encodedAddress.Length));
            buffer.Advance(encodedAddress.Length);
        }

        Dhcpv6MessageWriter.WriteOptions(buffer, Options, nameof(Options));

        return buffer.WrittenSpan.ToArray();
    }
}
