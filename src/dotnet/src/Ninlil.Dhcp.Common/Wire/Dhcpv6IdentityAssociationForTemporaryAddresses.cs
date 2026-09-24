using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.5's Identity Association for Temporary
///     Addresses (IA_TA) option -- the same nested-<see cref="Dhcpv6IdentityAssociationAddress" />
///     shape as <see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses" />, but with no
///     T1/T2 renewal timers of its own: RFC 8415 Section 21.5 deliberately omits them because a
///     temporary address is never renewed, only re-requested from scratch.
/// </summary>
public class Dhcpv6IdentityAssociationForTemporaryAddresses
{
    /// <summary>
    ///     This constant contains the fixed portion of this option's own wire size -- just the
    ///     IAID -- before its own nested options area.
    /// </summary>
    private const int FixedLength = 4;

    /// <summary>
    ///     This property contains the client-chosen Identity Association Identifier that names
    ///     this specific association, unique among all of this client's own IA_NA/IA_TA/IA_PD
    ///     associations.
    /// </summary>
    public uint IdentityAssociationId { get; set; }

    /// <summary>
    ///     This property contains every address assigned to (or requested for) this association,
    ///     decoded from this option's own nested <see cref="Dhcpv6IdentityAssociationAddress" />
    ///     options.
    /// </summary>
    public List<Dhcpv6IdentityAssociationAddress> Addresses { get; } = [];

    /// <summary>
    ///     This property contains every other nested option this association carries (most
    ///     commonly a Status Code option).
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IA_TA option's own value bytes (everything after its own
    ///     option-code/option-len header).
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
    ///     This method encodes this option's own value bytes (everything after its own
    ///     option-code/option-len header, which the caller -- <see cref="Dhcpv6MessageOptionExtensions" />
    ///     -- adds separately).
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
