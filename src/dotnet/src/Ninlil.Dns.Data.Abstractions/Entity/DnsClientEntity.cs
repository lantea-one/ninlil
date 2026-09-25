using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one known device: the client registry that per-client filter-list
///     assignments, parental-control schedules, rewrite scoping and the per-client activity view
///     attach to. A device is found by a persistent identifier it answers to (a MAC address, a
///     DUID, or both for a dual-stack device), not by source IP address, since an address can move
///     to another device. The identifiers are <see cref="DnsClientAddressEntity" /> rows, and
///     <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> resolves a query's
///     source address to one of them.
///     <para>
///         <strong>No dependency on Ninlil's DHCP server.</strong> A client can exist for a
///         device that never had a lease from this server (one served by an ISP router, or with a
///         static address), because the ARP/neighbor-table resolver identifies clients whichever
///         DHCP server, if any, assigned the address.
///     </para>
/// </summary>
public class DnsClientEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the display name of this client. DHCP auto-population may fill it
    ///     from the host name a lease presents, and an operator can always rename it.
    ///     <see langword="null" /> until set.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    ///     This property contains the <see cref="DnsClientGroupEntity" /> this client is assigned to,
    ///     overriding the group its address would match through <see cref="DnsClientGroupNetworkEntity" />.
    ///     <see langword="null" /> means no override, so network matching applies.
    /// </summary>
    public Guid? ClientGroupId { get; set; }

    /// <summary>
    ///     This property indicates whether this client is disabled. A disabled client is treated as
    ///     unresolved wherever <c>IDnsClientRepository.GetByClientIdentifierAsync</c> feeds a decision
    ///     (filtering, schedules, rewrite scoping, pause), so neither its own settings nor its
    ///     group's apply. That differs from <see cref="PausedUntilUtc" />: disabling makes the
    ///     client's settings inert, while pausing blocks resolution outright.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains the time, in UTC, until which DNS resolution is blocked for this
    ///     client: a one-click, time-limited "instant pause" that overrides every other mechanism
    ///     (schedules, filter lists, rewrites). <see langword="null" /> or a past time means not
    ///     paused. No sweep clears an elapsed value, because it is compared with the current time
    ///     when a query is answered; <c>Ninlil.Dns.Server.ClientSchedule.DnsClientScheduleEngine</c>
    ///     checks it before any schedule.
    /// </summary>
    public DateTimeOffset? PausedUntilUtc { get; set; }

    /// <summary>
    ///     This property contains free-text operator notes about this client.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property records how this row was created: by an operator, or by a DHCP lease grant
    ///     (see <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpClientRegistryAutoPopulationService</c>). It
    ///     defaults to <see cref="DnsClientOriginEnum.Manual" />, which is what the REST API
    ///     creates.
    /// </summary>
    public DnsClientOriginEnum Origin { get; set; } = DnsClientOriginEnum.Manual;

    /// <summary>
    ///     This property contains the device icon an operator chose for this client, or
    ///     <see langword="null" /> if none. Only an explicit choice is stored. A vendor-based
    ///     suggestion is computed when read (see
    ///     <c>Ninlil.Dns.Server.ClientIdentity.Vendor.MacVendorDeviceIconSuggester</c>), so a newer
    ///     vendor registry takes effect automatically and a suggestion is never mistaken for an
    ///     operator's choice.
    /// </summary>
    public DnsClientDeviceIconEnum? DeviceIconKey { get; set; }

    /// <summary>
    ///     This property indicates whether an operator starred this client as a favorite. It only
    ///     affects display and list filtering.
    /// </summary>
    public bool IsFavorite { get; set; }
}
