using System.Buffers;
using System.Buffers.Binary;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class represents RFC 8415 Section 21.21's Identity Association for Prefix Delegation
///     (IA_PD) option -- the Prefix Delegation analogue of
///     <see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses" />: a container identified by
///     a client-chosen IAID, carrying the same T1/T2 renewal timers plus a nested options area,
///     but nesting <see cref="Dhcpv6IdentityAssociationPrefix" /> options rather than
///     <see cref="Dhcpv6IdentityAssociationAddress" /> ones.
/// </summary>
public class Dhcpv6IdentityAssociationForPrefixDelegation
{
    /// <summary>
    ///     This constant contains the fixed portion of this option's own wire size -- the IAID
    ///     plus the two 4-byte T1/T2 fields -- before its own nested options area.
    /// </summary>
    private const int FixedLength = 12;

    /// <summary>
    ///     This property contains the client-chosen Identity Association Identifier that names
    ///     this specific association, unique among all of this client's own IA_NA/IA_TA/IA_PD
    ///     associations.
    /// </summary>
    public uint IdentityAssociationId { get; set; }

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, the client
    ///     should wait before contacting the server that delegated these prefixes to renew them
    ///     -- RFC 8415 Section 21.21's own "T1" field, in seconds on the wire.
    /// </summary>
    public TimeSpan T1 { get; set; }

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, the client
    ///     should wait before contacting any available server to renew these prefixes -- RFC 8415
    ///     Section 21.21's own "T2" field, in seconds on the wire.
    /// </summary>
    public TimeSpan T2 { get; set; }

    /// <summary>
    ///     This property contains every prefix delegated to (or requested for) this association,
    ///     decoded from this option's own nested <see cref="Dhcpv6IdentityAssociationPrefix" />
    ///     options.
    /// </summary>
    public List<Dhcpv6IdentityAssociationPrefix> Prefixes { get; } = [];

    /// <summary>
    ///     This property contains every other nested option this association carries (most
    ///     commonly a Status Code option).
    /// </summary>
    public List<Dhcpv6Option> Options { get; } = [];

    /// <summary>
    ///     This method decodes an IA_PD option's own value bytes (everything after its own
    ///     option-code/option-len header).
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
    ///     This method encodes this option's own value bytes (everything after its own
    ///     option-code/option-len header, which the caller -- <see cref="Dhcpv6MessageOptionExtensions" />
    ///     -- adds separately).
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
