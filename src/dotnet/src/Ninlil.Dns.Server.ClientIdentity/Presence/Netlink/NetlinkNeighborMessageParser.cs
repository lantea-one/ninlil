using System.Buffers.Binary;
using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Presence.Netlink;

/// <summary>
///     This record is one kernel neighbor-table entry: an IP address, the link-layer address the
///     kernel has resolved it to, and the kernel's own reachability state for it.
/// </summary>
/// <param name="Address">The neighbor's IP address (IPv4 via ARP, or IPv6 via NDP).</param>
/// <param name="LinkLayerAddress">The neighbor's link-layer address, or <see langword="null" /> when unresolved.</param>
/// <param name="State">The kernel's neighbor-unreachability-detection state bits (<c>NUD_*</c>).</param>
public sealed record NeighborTableEntry(IPAddress Address, byte[] LinkLayerAddress, ushort State)
{
    /// <summary>
    ///     This property indicates whether the kernel has positively confirmed the neighbor
    ///     reachable recently (<c>NUD_REACHABLE</c>). Deliberately the only state counted as a
    ///     sighting: <c>STALE</c> persists indefinitely on a small network after a device has
    ///     left, and <c>DELAY</c>/<c>PROBE</c> mean only that this host <em>sent</em> to a stale
    ///     entry and is still awaiting a reply -- exactly what traffic toward a departed device
    ///     looks like.
    /// </summary>
    public bool IsConfirmedReachable => (State & NetlinkNeighborMessageParser.NudReachable) != 0;
}

/// <summary>
///     This static class parses the reply to an rtnetlink neighbor dump (<c>RTM_GETNEIGH</c> with
///     <c>NLM_F_DUMP</c>) into <see cref="NeighborTableEntry" /> values. It is deliberately free of
///     any socket code, so it can be exercised directly against constructed byte sequences.
///     <para>
///         Every layout below is taken from the Linux <c>netlink(7)</c> and <c>rtnetlink(7)</c>
///         manual pages. A reply is a sequence of messages, each a 16-octet <c>nlmsghdr</c>
///         (<c>u32 len, u16 type, u16 flags, u32 seq, u32 pid</c>) padded to a 4-octet boundary; a
///         neighbor message's payload is a 12-octet <c>ndmsg</c>
///         (<c>u8 family, u8 pad, u16 pad, s32 ifindex, u16 state, u8 flags, u8 type</c>) followed
///         by 4-octet-aligned <c>rtattr</c> attributes (<c>u16 len, u16 type</c>). Netlink uses the
///         host's native byte order throughout; every supported target is little-endian.
///     </para>
/// </summary>
public static class NetlinkNeighborMessageParser
{
    /// <summary>The <c>nlmsghdr</c> type marking a multi-part reply's end (<c>NLMSG_DONE</c>).</summary>
    public const ushort MessageTypeDone = 3;

    /// <summary>The <c>nlmsghdr</c> type carrying an error or acknowledgement (<c>NLMSG_ERROR</c>).</summary>
    public const ushort MessageTypeError = 2;

    /// <summary>The <c>nlmsghdr</c> type of one neighbor entry (<c>RTM_NEWNEIGH</c>).</summary>
    public const ushort MessageTypeNewNeighbor = 28;

    /// <summary>The <c>nlmsghdr</c> type requesting neighbor entries (<c>RTM_GETNEIGH</c>).</summary>
    public const ushort MessageTypeGetNeighbor = 30;

    /// <summary>The neighbor state bit for a recently confirmed-reachable entry (<c>NUD_REACHABLE</c>).</summary>
    public const ushort NudReachable = 0x02;

    /// <summary>The length of an <c>nlmsghdr</c>.</summary>
    public const int HeaderLength = 16;

    /// <summary>The length of an <c>ndmsg</c>.</summary>
    public const int NeighborMessageLength = 12;

    /// <summary>The attribute type carrying the neighbor's IP address (<c>NDA_DST</c>).</summary>
    private const ushort AttributeDestination = 1;

    /// <summary>The attribute type carrying the neighbor's link-layer address (<c>NDA_LLADDR</c>).</summary>
    private const ushort AttributeLinkLayerAddress = 2;

    /// <summary>The Linux address family number for IPv4 (<c>AF_INET</c>).</summary>
    private const byte FamilyIPv4 = 2;

    /// <summary>The Linux address family number for IPv6 (<c>AF_INET6</c>).</summary>
    private const byte FamilyIPv6 = 10;

    /// <summary>
    ///     This method builds the one request message a neighbor dump needs: an
    ///     <c>RTM_GETNEIGH</c> header flagged <c>NLM_F_REQUEST | NLM_F_DUMP</c>, followed by an
    ///     <c>ndmsg</c> whose unspecified family asks for both IPv4 and IPv6 neighbors.
    /// </summary>
    /// <param name="sequence">The sequence number to stamp on the request.</param>
    /// <returns>The request bytes.</returns>
    public static byte[] BuildDumpRequest(uint sequence)
    {
        byte[] request = new byte[HeaderLength + NeighborMessageLength];
        BinaryPrimitives.WriteUInt32LittleEndian(request, (uint)request.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4), MessageTypeGetNeighbor);
        // NLM_F_REQUEST (0x1) | NLM_F_ROOT (0x100) | NLM_F_MATCH (0x200).
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(6), 0x0301);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), sequence);
        // pid 0 addresses the kernel; the ndmsg that follows is all zero (family AF_UNSPEC).
        return request;
    }

    /// <summary>
    ///     This method parses one received datagram, appending every neighbor entry it carries.
    /// </summary>
    /// <param name="datagram">One datagram read from the netlink socket.</param>
    /// <param name="entries">The list parsed entries are appended to.</param>
    /// <returns><see langword="true" /> when the datagram ends the dump (<c>NLMSG_DONE</c> or an error).</returns>
    public static bool ParseDatagram(ReadOnlySpan<byte> datagram, List<NeighborTableEntry> entries)
    {
        int offset = 0;
        while (offset + HeaderLength <= datagram.Length)
        {
            ReadOnlySpan<byte> header = datagram[offset..];
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(header);
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);

            // A length that cannot hold its own header, or overruns the datagram, is malformed;
            // stop rather than misread whatever follows.
            if (length < HeaderLength || offset + length > datagram.Length)
                return true;

            if (type is MessageTypeDone or MessageTypeError)
                return true;

            if (type == MessageTypeNewNeighbor)
            {
                NeighborTableEntry entry = ParseNeighbor(
                    datagram.Slice(offset + HeaderLength, length - HeaderLength)
                );
                if (entry is not null)
                    entries.Add(entry);
            }

            offset += Align(length);
        }

        return false;
    }

    /// <summary>
    ///     This method parses one <c>ndmsg</c> payload and its attributes, or returns
    ///     <see langword="null" /> for an entry with no usable IP address.
    /// </summary>
    private static NeighborTableEntry ParseNeighbor(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < NeighborMessageLength)
            return null;

        byte family = payload[0];
        ushort state = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]);

        IPAddress address = null;
        byte[] linkLayerAddress = null;

        int offset = NeighborMessageLength;
        while (offset + 4 <= payload.Length)
        {
            int attributeLength = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            // Mask off NLA_F_NESTED/NLA_F_NET_BYTEORDER, which share the type field's top bits.
            int attributeType =
                BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + 2)..]) & 0x3FFF;
            if (attributeLength < 4 || offset + attributeLength > payload.Length)
                break;

            ReadOnlySpan<byte> value = payload.Slice(offset + 4, attributeLength - 4);
            if (attributeType == AttributeDestination)
                address = ToAddress(family, value);
            else if (attributeType == AttributeLinkLayerAddress && value.Length > 0)
                linkLayerAddress = value.ToArray();

            offset += Align(attributeLength);
        }

        return address is null ? null : new NeighborTableEntry(address, linkLayerAddress, state);
    }

    /// <summary>
    ///     This method converts an <c>NDA_DST</c> value to an address of the message's family.
    /// </summary>
    private static IPAddress ToAddress(byte family, ReadOnlySpan<byte> value) =>
        (family, value.Length) switch
        {
            (FamilyIPv4, 4) => new IPAddress(value),
            (FamilyIPv6, 16) => new IPAddress(value),
            _ => null,
        };

    /// <summary>
    ///     This method rounds a length up to netlink's 4-octet alignment.
    /// </summary>
    private static int Align(int length) => (length + 3) & ~3;
}
