using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to register a new known client device
///     (<c>POST /api/clients</c>) together with its first address -- <see cref="ClientIdentifier" />/
///     <see cref="ProtocolVersion" /> become that address's own entire identity, immutable
///     afterward exactly like every other address (see <see cref="ClientsController" />'s own doc
///     comment); a second, or further, address is added later via
///     <see cref="Controllers.ClientAddressesController.Create" />, never through this request.
///     See <see cref="ClientWriteRequest" /> (used for <c>PUT</c>/<c>PATCH</c> instead) for the
///     device's own fields that remain editable.
/// </summary>
/// <param name="ClientIdentifier">A colon-separated MAC address for an IPv4 client, or a DUID for an IPv6 one.</param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs to.</param>
/// <param name="DisplayName">An optional, operator-chosen, human-readable name for this client.</param>
/// <param name="ClientGroupId">
///     The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity" /> this client is
///     explicitly assigned to, or <see langword="null" /> for no explicit override.
/// </param>
/// <param name="IsDisabled">Whether the client starts out disabled.</param>
/// <param name="Comments">Optional free-text operator notes about this client.</param>
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
///     This class carries the fields used to fully replace an existing known client's own
///     editable metadata (<c>PUT /api/clients/{id}</c>) -- deliberately omitting
///     <see cref="ClientCreateRequest.ClientIdentifier" />/<see cref="ClientCreateRequest.ProtocolVersion" />,
///     since <see cref="ClientsController" /> never allows either to change once a row exists. To
///     actually change a client's own identifier, delete this row and create a new one -- the
///     same "immutable field, delete and recreate" precedent
///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.TsigKeyEntity" />'s own shared secret already
///     establishes.
/// </summary>
/// <param name="DisplayName">An operator-chosen, human-readable name for this client.</param>
/// <param name="ClientGroupId">
///     The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity" /> this client is
///     explicitly assigned to, or <see langword="null" /> for no explicit override.
/// </param>
/// <param name="IsDisabled">Whether the client is currently disabled.</param>
/// <param name="Comments">Optional free-text operator notes about this client.</param>
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
///     This class carries a known client device's own full metadata, for list, single-item, and
///     creation responses -- including every address (<see cref="Addresses" />) it currently
///     answers to, since a real device may have more than one at once (a dual-stack device's own
///     separate IPv4 MAC address and IPv6 DUID, for instance).
/// </summary>
/// <param name="Id">This device's own unique identifier.</param>
/// <param name="Addresses">Every persistent identifier this device currently answers to -- see <see cref="ClientAddressDetail" />.</param>
/// <param name="DisplayName">An operator-chosen, human-readable name for this client.</param>
/// <param name="ClientGroupId">
///     The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientGroupEntity" /> this client is
///     explicitly assigned to, or <see langword="null" /> for no explicit override.
/// </param>
/// <param name="IsDisabled">Whether the client is currently disabled.</param>
/// <param name="Comments">Optional free-text operator notes about this client.</param>
/// <param name="Origin">How this row first came to exist -- an operator typing it in directly, or a DHCP lease grant auto-populating it.</param>
/// <param name="CreatedAtUtc">When this row was first created.</param>
/// <param name="UpdatedAtUtc">When this row was last updated, or <see langword="null" /> if never.</param>
/// <param name="PausedUntilUtc">
///     The instant, in UTC, until which this client's own instant pause blocks all of its DNS
///     resolution -- see <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientEntity.PausedUntilUtc" />.
///     <see langword="null" /> when this client is not currently paused.
/// </param>
/// <param name="IsCurrentlyPaused">
///     Whether <paramref name="PausedUntilUtc" /> is currently in effect -- computed at read time
///     against the current instant, since an elapsed pause is never actively cleared (see that
///     property's own doc comment for why no cleanup sweep is needed).
/// </param>
/// <param name="LastSeenUtc">
///     The most recent passive sighting of this device across every one of its addresses (DHCP,
///     DNS query, or confirmed-reachable neighbor-table activity), or <see langword="null" /> if it
///     has never been seen.
/// </param>
/// <param name="IsOnline">
///     Whether <paramref name="LastSeenUtc" /> falls within the configured online window --
///     computed at read time, never stored, so it turns false on its own once a device goes quiet.
/// </param>
/// <param name="DeviceIconKey">
///     The device icon an operator explicitly chose, or <see langword="null" /> when none was.
/// </param>
/// <param name="SuggestedDeviceIconKey">
///     A device icon suggested from <paramref name="Vendor" /> -- computed at read time, never
///     stored, and reported whether or not an explicit choice exists, so a caller can show
///     "suggested" apart from "chosen". <see langword="null" /> when the vendor gives no reliable
///     hint. A display falls back to it only when <paramref name="DeviceIconKey" /> is unset.
/// </param>
/// <param name="IsFavorite">Whether an operator has starred this client as a favorite.</param>
/// <param name="Vendor">
///     The hardware vendor behind this device, from the first of its addresses whose MAC address
///     resolves in the IEEE registries -- computed at read time, never stored, so it cannot go
///     stale. <see langword="null" /> when no address yields one.
/// </param>
/// <param name="IsRandomizedMacAddress">
///     Whether every MAC address this device's identifiers embed is a randomized (locally
///     administered) one -- reported instead of a vendor, since such an address names no
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
///     This class carries the known-client registry's headline counts
///     (<c>GET /api/clients/summary</c>), each computed fresh by a pushed-down count query against
///     the same filters <c>GET /api/clients</c> accepts, so a count and the list it links to can
///     never disagree about which devices it means.
/// </summary>
/// <param name="TotalCount">Every known client.</param>
/// <param name="OnlineCount">Clients with any address seen within <paramref name="OnlineWindow" />.</param>
/// <param name="OfflineCount">Clients not seen within <paramref name="OnlineWindow" />, including clients never seen at all.</param>
/// <param name="NewCount">Clients registered at or after <paramref name="NewSinceUtc" />.</param>
/// <param name="FavoriteCount">Clients an operator has starred as a favorite.</param>
/// <param name="NewSinceUtc">The instant <paramref name="NewCount" /> counts from.</param>
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
///     This class carries a single persistent identifier a known client device answers to, for
///     inclusion in <see cref="ClientDetail.Addresses" /> and for
///     <see cref="Controllers.ClientAddressesController" />'s own list/single-item/creation
///     responses.
/// </summary>
/// <param name="Id">This address row's own unique identifier.</param>
/// <param name="ClientId">The owning device's own unique identifier.</param>
/// <param name="ClientIdentifier">A colon-separated MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs to.</param>
/// <param name="Address">
///     The most recently observed source IP address this identifier answered from, or
///     <see langword="null" /> if none has been recorded yet.
/// </param>
/// <param name="FirstSeenUtc">When this identifier was first recorded against this device.</param>
/// <param name="LastSeenUtc">
///     When this identifier was most recently observed, or <see langword="null" /> if no
///     presence-tracking signal has updated it yet.
/// </param>
/// <param name="CreatedAtUtc">When this address row was first created.</param>
/// <param name="MacAddress">
///     The MAC address <paramref name="ClientIdentifier" /> embeds (directly, or inside a DHCPv4
///     client-id or a link-layer DUID), as lowercase colon-separated hexadecimal, or
///     <see langword="null" /> when it embeds none.
/// </param>
/// <param name="Vendor">
///     The organization IEEE assigned <paramref name="MacAddress" />'s block to -- computed at read
///     time. <see langword="null" /> when there is no MAC, it is randomized, or no registry lists it.
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
///     This class carries the fields used to add a further address to an existing known client
///     device (<c>POST /api/clients/{clientId}/addresses</c>) -- the only way, alongside
///     <see cref="ClientCreateRequest" />'s own initial address, that a device ever gains an
///     identifier of its own. Rejected with <c>409 Conflict</c> when this exact identifier/protocol
///     pair already belongs to any device (this one included), the same real, storage-level
///     uniqueness <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientAddressEntity" />'s own
///     composite index enforces.
/// </summary>
/// <param name="ClientIdentifier">A colon-separated MAC address for an IPv4 identity, or a DUID for an IPv6 one.</param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs to.</param>
public record ClientAddressWriteRequest(
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion
);

/// <summary>
///     This class carries the fields used to start a new instant pause
///     (<c>POST /api/clients/{clientId}/pause</c> and its client-group counterpart) -- a
///     duration, rather than an explicit timestamp, since a one-click "15 min / 1 hour / until
///     tomorrow morning" UI is this feature's own primary use case, and a duration needs no
///     client-side time-zone/clock-skew reasoning at all: the server always computes
///     <c>PausedUntilUtc</c> as its own current instant plus this duration.
/// </summary>
/// <param name="DurationMinutes">
///     How many minutes, from now, to pause for. Must be a positive number -- zero or negative is
///     rejected with a <c>400</c> <see cref="Ninlil.Dns.Common.Exceptions.ValidationException" />.
/// </param>
public record InstantPauseRequest(int DurationMinutes);

/// <summary>
///     This class carries the fields used to create a new filter-list assignment for a known
///     client (<c>POST /api/clients/{clientId}/list-assignments</c>) -- mirrors
///     <see cref="ClientGroupListAssignmentWriteRequest" /> exactly, keyed by client instead of
///     client group. There is deliberately no corresponding <c>PUT</c>/<c>PATCH</c> -- an
///     assignment is just a join between a client and a list with no mutable fields of its own;
///     changing which list is assigned means deleting one assignment and creating another.
/// </summary>
/// <param name="ListId">The <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsFilterListEntity" /> to assign to this client.</param>
public record ClientListAssignmentWriteRequest(Guid ListId);

/// <summary>
///     This class carries a single client list assignment's own fields, for list, single-item,
///     and creation responses.
/// </summary>
public record ClientListAssignmentDetail(
    Guid Id,
    Guid ClientId,
    Guid ListId,
    DateTimeOffset CreatedAtUtc
);
