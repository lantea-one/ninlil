using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Identity;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This class implements <see cref="IMacVendorLookupService" /> over one atomically swappable
///     <see cref="MacVendorTable" />.
///     <para>
///         At construction it loads both the snapshot embedded in this assembly and, when present,
///         the refreshed copy persisted at <see cref="MacVendorLookupOptions.DataFilePath" />, and
///         keeps whichever is newer -- so a restart keeps the newest downloaded data, while an
///         upgrade to a build carrying a newer embedded snapshot than the last download is not held
///         back by it. A persisted file that is missing, unreadable, or corrupt is logged and
///         ignored; the embedded snapshot always loads, so lookups never start out empty.
///     </para>
///     <para>
///         MAC extraction reuses <see cref="DhcpClientIdentifierLinkKeys" />, the same RFC-derived
///         reading of a stored identifier that links a dual-stack device's DHCPv4 and DHCPv6
///         identities: a plain MAC, an RFC 2132 hardware-type-1 client-id, or a DUID-LLT/DUID-LL
///         (directly, or inside an RFC 4361 client-id) carrying an Ethernet address.
///     </para>
/// </summary>
public sealed class MacVendorLookupService : IMacVendorLookupService
{
    /// <summary>
    ///     This constant contains the manifest resource name of the embedded snapshot.
    /// </summary>
    public const string EmbeddedSnapshotResourceName =
        "Ninlil.Dns.Server.ClientIdentity.Vendor.Resources.mac-vendors.snapshot.br";

    /// <summary>The prefix <see cref="DhcpClientIdentifierLinkKeys" /> gives a MAC-derived link key.</summary>
    private const string MacLinkKeyPrefix = "mac:";

    /// <summary>The table currently answering lookups; replaced wholesale, never mutated.</summary>
    private MacVendorTable _current;

    /// <summary>
    ///     This constructor loads the newest available table -- see this class's own doc comment.
    /// </summary>
    /// <param name="options">The vendor lookup options, read once for the persisted data path.</param>
    /// <param name="logger">The logger a failed load of the persisted file is reported to.</param>
    public MacVendorLookupService(
        IOptions<MacVendorLookupOptions> options,
        ILogger<MacVendorLookupService> logger
    )
    {
        MacVendorTable embedded = LoadEmbeddedSnapshot();
        MacVendorTable persisted = TryLoadPersisted(options.Value.DataFilePath, logger);

        _current =
            persisted is not null && persisted.GeneratedAtUtc > embedded.GeneratedAtUtc
                ? persisted
                : embedded;
    }

    /// <inheritdoc />
    public MacVendorTable Current => Volatile.Read(ref _current);

    /// <summary>
    ///     This method atomically replaces the table answering lookups -- every lookup already in
    ///     flight finishes against the table it started with.
    /// </summary>
    /// <param name="table">The new table.</param>
    public void Replace(MacVendorTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Volatile.Write(ref _current, table);
    }

    /// <inheritdoc />
    public MacVendorLookupResult LookupClientIdentifier(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion
    )
    {
        byte[] macAddress = ExtractMacAddress(clientIdentifier, protocolVersion);
        return macAddress is null
            ? MacVendorLookupResult.NoMacAddress
            : LookupMacAddress(macAddress);
    }

    /// <inheritdoc />
    public MacVendorLookupResult LookupMacAddress(ReadOnlySpan<byte> macAddress)
    {
        if (macAddress.Length != 6)
            return MacVendorLookupResult.NoMacAddress;

        string formatted = string.Join(
            ':',
            macAddress.ToArray().Select(octet => octet.ToString("x2"))
        );

        // The locally-administered (U/L) bit is bit 1 of the first octet (IEEE 802-2014 clause
        // 8.2.2): set, the address was assigned by the device or its administrator rather than
        // drawn from a manufacturer's block, so its prefix identifies nobody.
        if ((macAddress[0] & 0x02) != 0)
            return new MacVendorLookupResult(formatted, null, true);

        return new MacVendorLookupResult(formatted, Current.Lookup(macAddress), false);
    }

    /// <summary>
    ///     This method extracts the MAC address a stored client identifier embeds.
    /// </summary>
    /// <param name="clientIdentifier">The stored identifier.</param>
    /// <param name="protocolVersion">Which identifier space it belongs to.</param>
    /// <returns>The 6-octet MAC address, or <see langword="null" /> when the identifier embeds none.</returns>
    public static byte[] ExtractMacAddress(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion
    )
    {
        IReadOnlySet<string> linkKeys =
            protocolVersion == InternetProtocolVersionEnum.IPv6
                ? DhcpClientIdentifierLinkKeys.FromDhcpv6Identifier(clientIdentifier)
                : DhcpClientIdentifierLinkKeys.FromDhcpv4Identifier(clientIdentifier);

        // An identifier embeds at most one MAC, so at most one such key exists.
        string macKey = linkKeys.FirstOrDefault(key =>
            key.StartsWith(MacLinkKeyPrefix, StringComparison.Ordinal)
        );

        return macKey is null ? null : Convert.FromHexString(macKey[MacLinkKeyPrefix.Length..]);
    }

    /// <summary>
    ///     This method loads the snapshot compiled into this assembly.
    /// </summary>
    /// <returns>The embedded table.</returns>
    public static MacVendorTable LoadEmbeddedSnapshot()
    {
        using Stream stream =
            typeof(MacVendorLookupService).Assembly.GetManifestResourceStream(
                EmbeddedSnapshotResourceName
            )
            ?? throw new InvalidOperationException(
                $"The embedded MAC vendor snapshot '{EmbeddedSnapshotResourceName}' is missing from this build."
            );

        return MacVendorSnapshotCodec.Read(stream, MacVendorDataSourceEnum.Embedded);
    }

    /// <summary>
    ///     This method loads the persisted, previously downloaded table, or returns
    ///     <see langword="null" /> when there is none or it cannot be read.
    /// </summary>
    private static MacVendorTable TryLoadPersisted(string path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using FileStream stream = File.OpenRead(path);
            return MacVendorSnapshotCodec.Read(stream, MacVendorDataSourceEnum.Downloaded);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or InvalidDataException
                        or FormatException
                        or UnauthorizedAccessException
            )
        {
            logger.LogWarning(
                exception,
                "Ignoring the persisted MAC vendor data at '{Path}'; using the embedded snapshot instead.",
                path
            );
            return null;
        }
    }
}
