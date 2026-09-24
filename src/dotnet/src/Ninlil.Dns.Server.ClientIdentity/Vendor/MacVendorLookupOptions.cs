// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This class holds the settings for MAC vendor lookup's own automatic data refresh. Lookups
///     themselves need no configuration: a snapshot of the IEEE registries is compiled into this
///     assembly, so a vendor resolves offline from the very first boot.
///     <para>
///         <b>Automatic refresh is on by default.</b> The IEEE assigns new blocks continually, and
///         this server's deployment target is a homelab box left running for years; a compiled-in
///         snapshot alone would drift further out of date with every month nobody upgrades it.
///         Refreshing is a plain, conditional HTTPS download of three public files on a slow
///         schedule -- no new listening surface and nothing sent about the local network -- so it
///         follows this project's "self-maintaining features default on" rule. A failed or
///         implausible download never replaces the data already loaded.
///     </para>
///     <para>
///         Every value except <see cref="DataFilePath" /> is read live through
///         <c>IOptionsMonitor</c>. <see cref="DataFilePath" /> is read once at startup and is
///         deliberately configuration-file-only, like every other on-disk state path.
///     </para>
/// </summary>
public sealed class MacVendorLookupOptions
{
    /// <summary>
    ///     This constant contains the configuration section these options are bound from.
    /// </summary>
    public const string SectionName = "DnsServer:MacVendorLookup";

    /// <summary>
    ///     This constant contains the IEEE's own URL for the MA-L (24-bit OUI) registry.
    /// </summary>
    public const string DefaultMaLRegistryUrl = "https://standards-oui.ieee.org/oui/oui.csv";

    /// <summary>
    ///     This constant contains the IEEE's own URL for the MA-M (28-bit) registry.
    /// </summary>
    public const string DefaultMaMRegistryUrl = "https://standards-oui.ieee.org/oui28/mam.csv";

    /// <summary>
    ///     This constant contains the IEEE's own URL for the MA-S (36-bit) registry.
    /// </summary>
    public const string DefaultMaSRegistryUrl = "https://standards-oui.ieee.org/oui36/oui36.csv";

    /// <summary>
    ///     This property indicates whether the registries are periodically re-downloaded from the
    ///     IEEE. On by default -- see this class's own doc comment.
    /// </summary>
    public bool EnableAutomaticRefresh { get; set; } = true;

    /// <summary>
    ///     This property contains how long to wait after a successful check before checking
    ///     again. Two weeks by default: block assignments only ever matter for devices that are
    ///     new to the network, and each check is a conditional request the IEEE server answers
    ///     with no body when nothing changed.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    ///     This property contains where the MA-L registry CSV is downloaded from.
    /// </summary>
    public string MaLRegistryUrl { get; set; } = DefaultMaLRegistryUrl;

    /// <summary>
    ///     This property contains where the MA-M registry CSV is downloaded from.
    /// </summary>
    public string MaMRegistryUrl { get; set; } = DefaultMaMRegistryUrl;

    /// <summary>
    ///     This property contains where the MA-S registry CSV is downloaded from.
    /// </summary>
    public string MaSRegistryUrl { get; set; } = DefaultMaSRegistryUrl;

    /// <summary>
    ///     This property contains where a successfully refreshed table is persisted, so a restart
    ///     keeps the newest data rather than falling back to the compiled-in snapshot. The refresh
    ///     bookkeeping (last attempt, last result) is kept beside it with a <c>.state.json</c>
    ///     suffix. Defaults to the same <c>data</c> directory beside the application every other
    ///     piece of local, non-database state uses.
    /// </summary>
    public string DataFilePath { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data", "mac-vendors.snapshot.br");

    /// <summary>
    ///     This method returns the configured download URL for one registry.
    /// </summary>
    /// <param name="registry">The registry whose URL to return.</param>
    /// <returns>The configured URL.</returns>
    public string GetRegistryUrl(MacVendorRegistryEnum registry) =>
        registry switch
        {
            MacVendorRegistryEnum.MaL => MaLRegistryUrl,
            MacVendorRegistryEnum.MaM => MaMRegistryUrl,
            MacVendorRegistryEnum.MaS => MaSRegistryUrl,
            _ => throw new ArgumentOutOfRangeException(nameof(registry)),
        };
}
