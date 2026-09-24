using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one known real-world device this server is tracking -- the
///     "known client" registry a later feature (per-client filter-list assignment, parental
///     control schedules, a per-client activity view) associates configuration and history with.
///     A device is looked up by whichever persistent identifier it currently answers to (a MAC
///     address, a DUID, or both at once for a genuinely dual-stack device) rather than by a source
///     IP address, since an address can be reassigned to a different physical device at any time;
///     see <see cref="DnsClientAddressEntity" /> for the one-to-many identifier/address relationship
///     this class no longer carries directly itself, and
///     <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> for how a query's
///     source address is resolved to one of those identifiers in the first place.
///     <para>
///         <strong>Deliberately DHCP-independent at the data-model level.</strong> A row here can
///         be created for a device this server has never issued a lease to at all -- a client on
///         an external router/ISP modem's own DHCP server, or one with a static address and no
///         DHCP involvement whatsoever -- since <c>IClientIdentityResolver</c>'s own
///         ARP/neighbor-table implementation resolves a client's identity independently of which
///         DHCP server (if any) actually assigned its address.
///     </para>
/// </summary>
public class DnsClientEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains an operator-editable, human-readable name for this client. A
    ///     later feature may auto-populate a default value here from a DHCP lease's own presented
    ///     host name, but that population logic is not this entity's own concern -- an operator
    ///     can always freely rename a client regardless of where its default name came from.
    ///     <see langword="null" /> until either an operator or a later feature sets one.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    ///     This property contains the <see cref="DnsClientGroupEntity" /> this client is
    ///     explicitly assigned to, overriding whatever group its own source address/network would
    ///     otherwise resolve to via <see cref="DnsClientGroupNetworkEntity" />. <see langword="null" />
    ///     means this client carries no explicit group override of its own -- ordinary
    ///     network-based group resolution still applies to it unchanged.
    /// </summary>
    public Guid? ClientGroupId { get; set; }

    /// <summary>
    ///     This property indicates whether this client is currently disabled -- a disabled client
    ///     is treated as unresolvable everywhere <c>IDnsClientRepository.GetByClientIdentifierAsync</c>
    ///     feeds a resolution (filtering, schedules, rewrite scoping, instant pause below): it
    ///     contributes neither its own direct configuration nor its group's. This is a distinct
    ///     concern from <see cref="PausedUntilUtc" /> -- disabling a client makes every one of its
    ///     own settings inert, while pausing a client (or leaving it fully enabled) still applies
    ///     every one of those settings, just with resolution itself outright blocked while active.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains the instant, in UTC, until which this client's own DNS
    ///     resolution is entirely blocked -- a one-click, time-boxed "instant pause" kill switch,
    ///     independent of and unconditionally overriding every other mechanism in this project
    ///     (time-of-day schedules, filter lists, rewrites, everything). <see langword="null" />, or
    ///     any instant that has already passed, means this client is not currently paused; no
    ///     background sweep ever clears an elapsed value, since comparing it against the current
    ///     instant at query-answering time is self-correcting the moment it lapses -- see
    ///     <c>Ninlil.Dns.Server.ClientSchedule.DnsClientScheduleEngine</c>, which checks this
    ///     ahead of every schedule window it also evaluates.
    /// </summary>
    public DateTimeOffset? PausedUntilUtc { get; set; }

    /// <summary>
    ///     This property contains free-text operator notes about this client.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property records how this row first came to exist -- an operator typing it in
    ///     directly, or a DHCP lease grant auto-populating it (see
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpClientRegistryAutoPopulationService</c>).
    ///     Defaults to <see cref="DnsClientOriginEnum.Manual" />, since every row created through
    ///     the ordinary REST API is exactly that.
    /// </summary>
    public DnsClientOriginEnum Origin { get; set; } = DnsClientOriginEnum.Manual;

    /// <summary>
    ///     This property contains the device icon an operator has explicitly chosen for this
    ///     client, or <see langword="null" /> when none has been chosen. Only an explicit choice is
    ///     ever stored here: a vendor-based suggestion is computed fresh at read time instead (see
    ///     <c>Ninlil.Dns.Server.ClientIdentity.Vendor.MacVendorDeviceIconSuggester</c>), so a
    ///     refreshed vendor registry or an improved heuristic is picked up automatically and a
    ///     suggestion can never be mistaken for an operator's own decision.
    /// </summary>
    public DnsClientDeviceIconEnum? DeviceIconKey { get; set; }

    /// <summary>
    ///     This property indicates whether an operator has starred this client as a favorite --
    ///     purely a display/filtering convenience with no effect on resolution, filtering, or any
    ///     other behavior.
    /// </summary>
    public bool IsFavorite { get; set; }
}
