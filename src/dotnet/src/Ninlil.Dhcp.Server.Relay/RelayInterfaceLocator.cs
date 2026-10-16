using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay;

/// <summary>
///     This class finds the network interface that holds a given address. The relay agents use it
///     to learn which interface faces the clients: <see cref="Dhcpv4RelayAgent" /> binds its
///     client-side socket to the interface that owns <c>giaddr</c>, and
///     <see cref="Dhcpv6RelayAgent" /> scopes a client's link-local address with the interface
///     that owns the link-address.
/// </summary>
internal static class RelayInterfaceLocator
{
    /// <summary>
    ///     This method searches the interfaces that are up for one whose unicast addresses
    ///     include <paramref name="address" />. An exact match wins. A DHCPv4 address that lies
    ///     inside the prefix of an interface's address also matches, which is how every
    ///     <c>127.0.0.0/8</c> address resolves to the loopback interface.
    /// </summary>
    /// <param name="address">The address to look for. Zone IDs are ignored.</param>
    /// <param name="interfaceIndex">The interface index (the operating system's scope ID for link-local addresses), or 0.</param>
    /// <param name="interfaceName">The interface name, as the operating system knows it, or <see langword="null" />.</param>
    /// <returns>True when an interface was found.</returns>
    public static bool TryFind(IPAddress address, out int interfaceIndex, out string interfaceName)
    {
        interfaceIndex = 0;
        interfaceName = null;

        if (address == null)
            return false;

        NetworkInterface prefixMatch = null;

        try
        {
            foreach (
                NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces()
            )
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (
                    UnicastIPAddressInformation unicast in networkInterface
                        .GetIPProperties()
                        .UnicastAddresses
                )
                {
                    if (ClientAddress.IsSameAddress(unicast.Address, address))
                        return Describe(
                            networkInterface,
                            address,
                            out interfaceIndex,
                            out interfaceName
                        );

                    if (prefixMatch == null && IsInsideIPv4Prefix(unicast, address))
                        prefixMatch = networkInterface;
                }
            }
        }
        catch (NetworkInformationException)
        {
            // The platform cannot list its interfaces; the callers treat this as "not found".
            return false;
        }

        return prefixMatch != null
            && Describe(prefixMatch, address, out interfaceIndex, out interfaceName);
    }

    /// <summary>
    ///     This method says whether an IPv4 address lies inside the prefix of an interface
    ///     address.
    /// </summary>
    private static bool IsInsideIPv4Prefix(UnicastIPAddressInformation unicast, IPAddress address)
    {
        if (
            unicast.Address.AddressFamily != AddressFamily.InterNetwork
            || address.AddressFamily != AddressFamily.InterNetwork
            || unicast.IPv4Mask == null
        )
            return false;

        byte[] network = unicast.Address.GetAddressBytes();
        byte[] mask = unicast.IPv4Mask.GetAddressBytes();
        byte[] candidate = address.GetAddressBytes();

        for (int index = 0; index < network.Length; index++)
        {
            if ((network[index] & mask[index]) != (candidate[index] & mask[index]))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     This method reads the index and name of an interface. The index comes from the IPv4 or
    ///     IPv6 properties by the address family; an interface without those properties (the
    ///     family is disabled on it) is reported as not found.
    /// </summary>
    private static bool Describe(
        NetworkInterface networkInterface,
        IPAddress address,
        out int interfaceIndex,
        out string interfaceName
    )
    {
        interfaceIndex = 0;
        interfaceName = null;

        try
        {
            IPInterfaceProperties properties = networkInterface.GetIPProperties();
            interfaceIndex =
                address.AddressFamily == AddressFamily.InterNetworkV6
                    ? properties.GetIPv6Properties().Index
                    : properties.GetIPv4Properties().Index;
        }
        catch (NetworkInformationException)
        {
            return false;
        }

        interfaceName = networkInterface.Name;
        return true;
    }
}
