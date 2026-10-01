using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.5's Identity Association for Temporary
///     Addresses (IA_TA) option. It nests <see cref="Dhcpv6IdentityAssociationAddress" /> options
///     like <see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses" />, but has no T1/T2
///     timers, since temporary addresses are not renewed.
/// </summary>
public class Dhcpv6IdentityAssociationForTemporaryAddresses
{
    /// <summary>
    ///     This constant contains the length of the fixed part (the IAID) before the nested
    ///     options.
    /// </summary>
    private const int FixedLength = 4;

    /// <summary>
    ///     This property contains the client-chosen Identity Association Identifier (IAID), unique
    ///     among the client's associations of this type.
    /// </summary>
    public uint IdentityAssociationId { get; set; }

    /// <summary>
    ///     This property contains the addresses assigned to (or requested for) this association,
    ///     decoded from its nested IA Address options.
    /// </summary>
    public List<Dhcpv6IdentityAssociationAddress> Addresses { get; } = [];

    /// <summary>
    ///     This property contains the other nested options (usually a Status Code option).
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IA_TA option's value (everything after the option header).
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        out Dhcpv6IdentityAssociationForTemporaryAddresses association,
        out string failureReason
    )
    {
        association = null;

        if (data.Length < FixedLength)
        {
            failureReason = "An IA_TA option is shorter than its own fixed 4-byte portion.";
            return false;
        }

        Dhcpv6IdentityAssociationForTemporaryAddresses result = new()
        {
            IdentityAssociationId = BinaryPrimitives.ReadUInt32BigEndian(data[..4]),
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
