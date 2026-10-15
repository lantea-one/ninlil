using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This class names the lease row that quarantines a declined address (RFC 2131 Section
///     4.3.3). A decline re-keys the declining client's row to <c>declined:&lt;address&gt;</c> so the
///     address stays blocked for every client while the client's own slot is free again.
///     <see cref="Dhcpv4LeaseAllocationService" /> writes and recognises these rows. The key
///     cannot collide with a client: <c>ClientIdentifierFormat</c> refuses it, and a scope holds
///     one row per address.
/// </summary>
internal static class DhcpDeclinedAddress
{
    /// <summary>
    ///     This constant contains the text every quarantine key starts with.
    /// </summary>
    public const string KeyPrefix = "declined:";

    /// <summary>
    ///     This method builds the client identifier of the quarantine row for an address.
    /// </summary>
    /// <param name="address">The declined address.</param>
    /// <returns>The prefix followed by the canonical text of the address.</returns>
    public static string KeyFor(IPAddress address) => KeyPrefix + address;

    /// <summary>
    ///     This method reports whether a client identifier is a quarantine key.
    /// </summary>
    /// <param name="clientIdentifier">The identifier stored on a lease row.</param>
    /// <returns><see langword="true" /> for a key built by <see cref="KeyFor" />.</returns>
    public static bool IsKey(string clientIdentifier) =>
        clientIdentifier is not null
        && clientIdentifier.StartsWith(KeyPrefix, StringComparison.Ordinal);
}
