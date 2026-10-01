using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to register a known client device
///     (<c>POST /api/clients</c>) with its first address. <see cref="ClientIdentifier" /> and
///     <see cref="ProtocolVersion" /> become that address's identity, which cannot change later
///     (see <see cref="ClientsController" />). Further addresses are added through
///     <see cref="Controllers.ClientAddressesController.Create" />. The device fields that stay
///     editable are in <see cref="ClientWriteRequest" />, used by <c>PUT</c> and <c>PATCH</c>.
/// </summary>
/// <param name="ClientIdentifier">A colon-separated MAC address for an IPv4 client, or a DUID for an IPv6 one.</param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs to.</param>
/// <param name="DisplayName">An optional, operator-chosen, human-readable name for the client.</param>
/// <param name="ClientGroupId">
///     The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity" /> the client is
///     explicitly assigned to, or <see langword="null" /> for none.
/// </param>
/// <param name="IsDisabled">Whether the client starts out disabled.</param>
/// <param name="Comments">Optional free-text operator notes about the client.</param>
/// <param name="DeviceIconKey">
///     An optional device icon, named by its <see cref="DnsClientDeviceIconEnum" /> value (for
///     example <c>Laptop</c>); any other value is rejected with a <c>400</c>. Omitted or
///     <see langword="null" /> leaves the choice unset, so the vendor-based suggestion applies.
/// </param>
/// <param name="IsFavorite">Whether the client starts out starred as a favorite.</param>
public record ClientCreateRequest(
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion,
    string DisplayName = null,
    Guid? ClientGroupId = null,
    bool IsDisabled = false,
    string Comments = null,
    string DeviceIconKey = null,
    bool IsFavorite = false
);

/// <summary>
///     This class carries the editable fields of a known client, replaced in full by
///     <c>PUT /api/clients/{id}</c>. It has no <see cref="ClientCreateRequest.ClientIdentifier" />
///     or <see cref="ClientCreateRequest.ProtocolVersion" />, because
///     <see cref="ClientsController" /> never lets an address's identity change. To change one, add
///     a new address and delete the old one, as with the shared secret of a
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.TsigKeyEntity" />.
/// </summary>
/// <param name="DisplayName">An operator-chosen, human-readable name for the client.</param>
/// <param name="ClientGroupId">
///     The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity" /> the client is
///     explicitly assigned to, or <see langword="null" /> for none.
/// </param>
/// <param name="IsDisabled">Whether the client is disabled.</param>
/// <param name="Comments">Optional free-text operator notes about the client.</param>
/// <param name="DeviceIconKey">
///     The device icon, named by its <see cref="DnsClientDeviceIconEnum" /> value, or
///     <see langword="null" /> to clear an explicit choice; any other value is rejected with a
///     <c>400</c>.
/// </param>
/// <param name="IsFavorite">Whether the client is starred as a favorite.</param>
public record ClientWriteRequest(
    string DisplayName,
    Guid? ClientGroupId,
    bool IsDisabled,
    string Comments,
    string DeviceIconKey = null,
    bool IsFavorite = false
);

/// <summary>
///     This class carries a known client device, as returned by the list, get and create
///     endpoints of <c>/api/clients</c>. <see cref="Addresses" /> lists every identifier the device
///     answers to, since one device can have several (a dual-stack device's IPv4 MAC address and
///     IPv6 DUID, for instance).
/// </summary>
/// <param name="Id">The device's unique identifier.</param>
/// <param name="Addresses">Every persistent identifier the device answers to (see <see cref="ClientAddressDetail" />).</param>
/// <param name="DisplayName">An operator-chosen, human-readable name for the client.</param>
/// <param name="ClientGroupId">
///     The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity" /> the client is
///     explicitly assigned to, or <see langword="null" /> for none.
/// </param>
/// <param name="IsDisabled">Whether the client is disabled.</param>
/// <param name="Comments">Optional free-text operator notes about the client.</param>
/// <param name="Origin">How the client was first registered: entered by an operator, or added automatically from a DHCP lease.</param>
/// <param name="CreatedAtUtc">When the client was registered.</param>
/// <param name="UpdatedAtUtc">When the client was last updated, or <see langword="null" /> if never.</param>
/// <param name="PausedUntilUtc">
///     The UTC instant until which the client's pause blocks all of its DNS resolution (see
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientEntity.PausedUntilUtc" />), or
///     <see langword="null" /> when no pause has been set.
/// </param>
/// <param name="IsCurrentlyPaused">
///     Whether <paramref name="PausedUntilUtc" /> is still in the future, computed when the
///     response is built. An expired pause is never cleared from storage, so this flag, not the
///     timestamp, says whether the client is paused.
/// </param>
/// <param name="LastSeenUtc">
///     The most recent passive sighting of the device on any of its addresses (DHCP, a DNS query,
///     or a reachable neighbor-table entry), or <see langword="null" /> if it has never been seen.
/// </param>
/// <param name="IsOnline">
///     Whether <paramref name="LastSeenUtc" /> falls within the configured online window. It is
///     computed when the response is built, so it turns false once a device goes quiet.
/// </param>
/// <param name="DeviceIconKey">
///     The device icon an operator explicitly chose, or <see langword="null" /> when none was.
/// </param>
/// <param name="SuggestedDeviceIconKey">
///     A device icon suggested from <paramref name="Vendor" />, computed when the response is
///     built. It is reported whether or not an explicit choice exists, so a caller can show
///     "suggested" apart from "chosen". <see langword="null" /> when the vendor gives no reliable
///     hint. A display falls back to it only when <paramref name="DeviceIconKey" /> is unset.
/// </param>
/// <param name="IsFavorite">Whether an operator has starred the client as a favorite.</param>
/// <param name="Vendor">
///     The hardware vendor of the device, from the first of its addresses whose MAC address
///     resolves in the IEEE registries. It is computed when the response is built, so it does not
///     go stale. <see langword="null" /> when no address yields one.
/// </param>
/// <param name="IsRandomizedMacAddress">
///     Whether every MAC address the device's identifiers embed is a randomized (locally
///     administered) one. It is reported instead of a vendor, since such an address names no
///     manufacturer. <see langword="false" /> when no identifier embeds a MAC at all.
/// </param>
public record ClientDetail(
    Guid Id,
    IReadOnlyList<ClientAddressDetail> Addresses,
    string DisplayName,
    Guid? ClientGroupId,
    bool IsDisabled,
    string Comments,
    DnsClientOriginEnum Origin,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? PausedUntilUtc,
    bool IsCurrentlyPaused,
    DateTimeOffset? LastSeenUtc,
    bool IsOnline,
    DnsClientDeviceIconEnum? DeviceIconKey = null,
    DnsClientDeviceIconEnum? SuggestedDeviceIconKey = null,
    bool IsFavorite = false,
    string Vendor = null,
    bool IsRandomizedMacAddress = false
);

/// <summary>
///     This class carries the headline counts of the known-client registry
///     (<c>GET /api/clients/summary</c>). Each is counted in storage with the same filters
///     <c>GET /api/clients</c> uses, so a count agrees with the list it links to.
/// </summary>
/// <param name="TotalCount">Every known client.</param>
/// <param name="OnlineCount">Clients with any address seen within <paramref name="OnlineWindow" />.</param>
/// <param name="OfflineCount">Clients not seen within <paramref name="OnlineWindow" />, including clients never seen; computed as total minus online.</param>
/// <param name="NewCount">Clients registered at or after <paramref name="NewSinceUtc" />.</param>
/// <param name="FavoriteCount">Clients an operator has starred as a favorite.</param>
/// <param name="NewSinceUtc">The instant <paramref name="NewCount" /> counts from: the request's <c>newSinceUtc</c>, or 24 hours ago when it was omitted.</param>
/// <param name="OnlineWindow">The online window the presence counts were evaluated against.</param>
public record ClientSummaryDetail(
    int TotalCount,
    int OnlineCount,
    int OfflineCount,
    int NewCount,
    int FavoriteCount,
    DateTimeOffset NewSinceUtc,
    TimeSpan OnlineWindow
);

/// <summary>
///     This class carries one persistent identifier a known client device answers to. It appears
///     in <see cref="ClientDetail.Addresses" /> and in the responses of
///     <see cref="Controllers.ClientAddressesController" />.
/// </summary>
/// <param name="Id">The address's unique identifier.</param>
/// <param name="ClientId">The owning device's unique identifier.</param>
/// <param name="ClientIdentifier">A colon-separated MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs to.</param>
/// <param name="Address">
///     The most recently observed source IP address this identifier answered from, or
///     <see langword="null" /> if none has been recorded yet.
/// </param>
/// <param name="FirstSeenUtc">When the identifier was first recorded against the device.</param>
/// <param name="LastSeenUtc">
///     When the identifier was last observed, or <see langword="null" /> if no presence signal
///     has updated it yet.
/// </param>
/// <param name="CreatedAtUtc">When the address was created.</param>
/// <param name="MacAddress">
///     The MAC address <paramref name="ClientIdentifier" /> embeds (directly, or inside a DHCPv4
///     client-id or a link-layer DUID), as lowercase colon-separated hexadecimal, or
///     <see langword="null" /> when it embeds none.
/// </param>
/// <param name="Vendor">
///     The organization IEEE assigned <paramref name="MacAddress" />'s block to, computed when the
///     response is built. <see langword="null" /> when there is no MAC, it is randomized, or no registry lists it.
/// </param>
/// <param name="IsRandomizedMacAddress">
///     Whether <paramref name="MacAddress" /> is a randomized (locally administered) address, which
///     is never looked up because it names no manufacturer.
/// </param>
public record ClientAddressDetail(
    Guid Id,
    Guid ClientId,
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion,
    string Address,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset CreatedAtUtc,
    string MacAddress = null,
    string Vendor = null,
    bool IsRandomizedMacAddress = false
);

/// <summary>
///     This class carries the fields used to add another address to a known client device
///     (<c>POST /api/clients/{clientId}/addresses</c>). Apart from the first address in
///     <see cref="ClientCreateRequest" />, this is the only way a device gains an identifier. The
///     request is rejected with <c>409 Conflict</c> when the identifier and protocol pair already
///     belongs to any device, this one included, matching the unique composite index on
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientAddressEntity" />.
/// </summary>
/// <param name="ClientIdentifier">A colon-separated MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs to.</param>
public record ClientAddressWriteRequest(
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion
);

/// <summary>
///     This class carries the fields used to start an instant pause
///     (<c>POST /api/clients/{id}/pause</c> and <c>POST /api/client-groups/{id}/pause</c>). It
///     takes a duration rather than a timestamp because the UI offers one-click choices such as
///     "15 minutes" or "1 hour", and a duration needs no reasoning about the caller's time zone or
///     clock. The server sets <c>PausedUntilUtc</c> to its current time plus the duration.
/// </summary>
/// <param name="DurationMinutes">
///     How many minutes from now to pause for. Zero or a negative number is rejected with a
///     <c>400</c> (<see cref="Ninlil.Dns.Common.Exceptions.ValidationException" />).
/// </param>
public record InstantPauseRequest(int DurationMinutes);

/// <summary>
///     This class carries the fields used to create a new filter-list assignment for a known
///     client (<c>POST /api/clients/{clientId}/list-assignments</c>), the client counterpart of
///     <see cref="ClientGroupListAssignmentWriteRequest" />. There is no <c>PUT</c> or
///     <c>PATCH</c>: an assignment only joins a client to a list, so assigning a different list
///     means deleting one assignment and creating another.
/// </summary>
/// <param name="ListId">The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsFilterListEntity" /> to assign to the client.</param>
public record ClientListAssignmentWriteRequest(Guid ListId);

/// <summary>
///     This class carries one client list assignment, as returned by the list, get and create
///     endpoints under <c>/api/clients/{clientId}/list-assignments</c>.
/// </summary>
public record ClientListAssignmentDetail(
    Guid Id,
    Guid ClientId,
    Guid ListId,
    DateTimeOffset CreatedAtUtc
);
