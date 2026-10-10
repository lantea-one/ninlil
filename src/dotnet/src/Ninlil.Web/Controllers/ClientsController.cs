using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Web.Examples.Clients;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the known-client registry (<see cref="DnsClientEntity" />): devices
///     an operator added here, or that
///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpClientRegistryAutoPopulationService</c> added the
///     first time a DHCP lease went to an unseen client identifier. It works with or without
///     DHCP. It also lists and counts clients by presence, and pauses or resumes a client's DNS.
///     Reads need <c>clients:read</c> and changes need <c>clients:write</c>.
///     <para>
///         <b>An address cannot be edited once created.</b>
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientAddressEntity.ClientIdentifier" />
///         and
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientAddressEntity.ProtocolVersion" />
///         together identify an address row: <c>IDnsClientRepository.GetByClientIdentifierAsync</c>
///         looks devices up by that pair, and DHCP auto-population uses it to decide whether a
///         device is new. Changing either would make a later lease for the original identifier miss
///         the row and create a duplicate device, and could produce a combination no protocol
///         issues (a DUID tagged <c>IPv4</c>, say). So <see cref="ClientWriteRequest" /> (for
///         <see cref="Replace" /> and <see cref="Patch" />) has no address field; to change one,
///         delete it (<see cref="ClientAddressesController.Delete" />) and add another
///         (<see cref="ClientAddressesController.Create" />). <see cref="Create" /> takes one
///         initial address, since a device without one could never be matched (see
///         <see cref="ClientCreateRequest" />).
///     </para>
///     <para>
///         <b>Vendor and suggested icon are worked out on every read, never stored.</b> Each
///         response looks up each address's embedded MAC address in the IEEE registries
///         (<see cref="IMacVendorLookupService" />) and suggests an icon from the result, so both
///         follow a registry refresh. Only the operator's
///         <see cref="DnsClientEntity.DeviceIconKey" /> choice and
///         <see cref="DnsClientEntity.IsFavorite" /> star are stored.
///     </para>
/// </summary>
/// <param name="clientRepository">Reads and writes clients.</param>
/// <param name="clientAddressRepository">Creates a new client's first address and lists each client's addresses.</param>
/// <param name="scheduleRepository">Removes a deleted client's schedules (see <see cref="Delete" />).</param>
/// <param name="listAssignmentRepository">Removes a deleted client's filter-list assignments (see <see cref="Delete" />).</param>
/// <param name="rewriteRepository">Removes the rewrites scoped to a deleted client (see <see cref="Delete" />).</param>
/// <param name="clientGroupRepository">Confirms a client's <see cref="ClientCreateRequest.ClientGroupId" /> names an existing group.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="presenceOptions">The current presence options, whose online window decides <see cref="ClientDetail.IsOnline" />.</param>
/// <param name="vendorLookup">Looks up the vendor of each address's MAC address.</param>
[ApiController]
[Route("api/clients")]
public class ClientsController(
    IDnsClientRepository clientRepository,
    IDnsClientAddressRepository clientAddressRepository,
    IDnsClientScheduleRepository scheduleRepository,
    IDnsClientListAssignmentRepository listAssignmentRepository,
    IDnsRewriteRepository rewriteRepository,
    IDnsClientGroupRepository clientGroupRepository,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<ClientPresenceOptions> presenceOptions,
    IMacVendorLookupService vendorLookup
) : ControllerBase
{
    /// <summary>
    ///     This action lists known clients, one page at a time, optionally narrowed by favorite
    ///     star (<c>isFavorite</c>), online or offline (<c>presence</c>) and registration time
    ///     (<c>createdSinceUtc</c>), in the requested order (<c>sort</c>; creation order by
    ///     default). Presence uses the online window at the moment of the request, the same cutoff
    ///     each device's <see cref="ClientDetail.IsOnline" /> uses. Filtering, ordering and paging
    ///     run in the storage engine. Requires <c>clients:read</c>.
    /// </summary>
    /// <param name="pagination">The page to return.</param>
    /// <param name="isFavorite">When set, only clients whose favorite star matches this value are listed.</param>
    /// <param name="presence">When set, only clients currently online, or currently offline, are listed.</param>
    /// <param name="createdSinceUtc">When set, only clients registered at or after this instant are listed.</param>
    /// <param name="sort">The order to list clients in; creation order when omitted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<ClientDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        [FromQuery] bool? isFavorite,
        [FromQuery] DnsClientPresenceFilterEnum? presence,
        [FromQuery] DateTimeOffset? createdSinceUtc,
        [FromQuery] DnsClientSortOrderEnum? sort,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // "Online" means seen at or after now minus the window, the same inclusive boundary
        // ClientPresenceOptions.IsOnline applies to each returned device.
        DateTimeOffset onlineCutoffUtc =
            DateTimeOffset.UtcNow - presenceOptions.CurrentValue.OnlineWindow;
        DnsClientSearchCriteria criteria = new(
            IsFavorite: isFavorite,
            SeenSinceUtc: presence == DnsClientPresenceFilterEnum.Online ? onlineCutoffUtc : null,
            NotSeenSinceUtc: presence == DnsClientPresenceFilterEnum.Offline
                ? onlineCutoffUtc
                : null,
            CreatedSinceUtc: createdSinceUtc,
            SortOrder: sort ?? DnsClientSortOrderEnum.Created
        );

        // The page and the total count are both computed by the storage engine
        // (WHERE/ORDER BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every client into
        // memory.
        IReadOnlyList<DnsClientEntity> clients = await clientRepository.SearchAsync(
            criteria,
            skip,
            take,
            stoppingToken
        );
        int totalCount = await clientRepository.CountAsync(criteria, stoppingToken);

        // Each client's addresses are loaded with one query per client on this page, all started
        // at once.
        ClientDetail[] details = await Task.WhenAll(
            clients.Select(client => ToDetailAsync(client, stoppingToken))
        );

        return Ok(new PagedResult<ClientDetail>(details, skip, take, totalCount));
    }

    /// <summary>
    ///     This action returns the registry's headline counts: total, online, offline, new and
    ///     favorites, each counted in storage with the same filters <see cref="List" /> uses, so a
    ///     count agrees with the matching list. Returns 400 when <c>newSinceUtc</c> is in the
    ///     future. Requires <c>clients:read</c>.
    /// </summary>
    /// <param name="newSinceUtc">
    ///     The instant "new" counts from. A caller showing "new today" passes its local midnight,
    ///     since the server does not know the viewer's time zone; when omitted, the last 24 hours
    ///     are counted.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpGet("summary")]
    [ReadFromReplica]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientSummaryDetailExample))]
    public async Task<ActionResult<ClientSummaryDetail>> Summary(
        [FromQuery] DateTimeOffset? newSinceUtc,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (newSinceUtc > now)
            throw new ValidationException("'newSinceUtc' cannot be in the future.");

        DateTimeOffset newSince = newSinceUtc ?? now.AddHours(-24);
        TimeSpan onlineWindow = presenceOptions.CurrentValue.OnlineWindow;
        DateTimeOffset onlineCutoffUtc = now - onlineWindow;

        // Sequential rather than parallel: the counts share this request's scoped repository, and a
        // DbContext does not allow two operations at once.
        int totalCount = await clientRepository.CountAsync(
            DnsClientSearchCriteria.All,
            stoppingToken
        );
        int onlineCount = await clientRepository.CountAsync(
            new DnsClientSearchCriteria(SeenSinceUtc: onlineCutoffUtc),
            stoppingToken
        );
        int newCount = await clientRepository.CountAsync(
            new DnsClientSearchCriteria(CreatedSinceUtc: newSince),
            stoppingToken
        );
        int favoriteCount = await clientRepository.CountAsync(
            new DnsClientSearchCriteria(IsFavorite: true),
            stoppingToken
        );

        // Offline is derived as total minus online for the same cutoff rather than counted, so a
        // device written between two queries cannot make the numbers disagree.
        return Ok(
            new ClientSummaryDetail(
                totalCount,
                onlineCount,
                totalCount - onlineCount,
                newCount,
                favoriteCount,
                newSince,
                onlineWindow
            )
        );
    }

    /// <summary>
    ///     This action returns one known client. Returns 404 for an unknown id. Requires
    ///     <c>clients:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientDetailExample))]
    public async Task<ActionResult<ClientDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        DnsClientEntity client = await clientRepository.GetByIdAsync(id, stoppingToken);
        if (client is null)
            throw new NotFoundException("Client", id.ToString());

        return Ok(await ToDetailAsync(client, stoppingToken));
    }

    /// <summary>
    ///     This action registers a client device with its first address. Returns 400 for a missing
    ///     identifier, an unknown client group or an unknown icon name, and 409 when a device
    ///     already has the same <see cref="ClientCreateRequest.ClientIdentifier" /> and
    ///     <see cref="ClientCreateRequest.ProtocolVersion" /> (the pair the address table's unique
    ///     index covers, checked first through
    ///     <see cref="IDnsClientRepository.GetByClientIdentifierAsync" />). Requires
    ///     <c>clients:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(typeof(ClientCreateRequest), typeof(ClientCreateRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ClientDetailExample))]
    public async Task<ActionResult<ClientDetail>> Create(
        ClientCreateRequest request,
        CancellationToken stoppingToken
    )
    {
        if (string.IsNullOrWhiteSpace(request?.ClientIdentifier))
            throw new ValidationException("'clientIdentifier' is required.");

        if (
            await clientRepository.GetByClientIdentifierAsync(
                request.ClientIdentifier,
                request.ProtocolVersion,
                stoppingToken
            )
            is not null
        )
            throw new ConflictException(
                $"A client already exists for identifier '{request.ClientIdentifier}' ({request.ProtocolVersion})."
            );

        if (
            request.ClientGroupId is { } clientGroupId
            && await clientGroupRepository.GetByIdAsync(clientGroupId, stoppingToken) is null
        )
            throw new ValidationException(
                $"'clientGroupId' does not name an existing client group ('{clientGroupId}')."
            );

        DnsClientDeviceIconEnum? deviceIconKey = ParseDeviceIconKey(request.DeviceIconKey);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DnsClientEntity client = new()
        {
            Id = Guid.NewGuid(),
            DisplayName = request.DisplayName,
            ClientGroupId = request.ClientGroupId,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            DeviceIconKey = deviceIconKey,
            IsFavorite = request.IsFavorite,
            CreatedAtUtc = now,
        };

        DnsClientAddressEntity address = new()
        {
            Id = Guid.NewGuid(),
            ClientId = client.Id,
            ClientIdentifier = request.ClientIdentifier,
            ProtocolVersion = request.ProtocolVersion,
            FirstSeenUtc = now,
            CreatedAtUtc = now,
        };

        await clientRepository.AddAsync(client, stoppingToken);
        await clientAddressRepository.AddAsync(address, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { id = client.Id },
            await ToDetailAsync(client, stoppingToken)
        );
    }

    /// <summary>
    ///     This action replaces a client's editable details; its addresses cannot be changed this
    ///     way (see the class summary). Returns 400 for an unknown client group or icon name and
    ///     404 for an unknown id. Requires <c>clients:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(typeof(ClientWriteRequest), typeof(ClientWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        ClientWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsClientEntity client = await clientRepository.GetByIdAsync(id, stoppingToken);
        if (client is null)
            throw new NotFoundException("Client", id.ToString());

        if (
            request?.ClientGroupId is { } clientGroupId
            && await clientGroupRepository.GetByIdAsync(clientGroupId, stoppingToken) is null
        )
            throw new ValidationException(
                $"'clientGroupId' does not name an existing client group ('{clientGroupId}')."
            );

        DnsClientDeviceIconEnum? deviceIconKey = ParseDeviceIconKey(request?.DeviceIconKey);

        client.DisplayName = request?.DisplayName;
        client.ClientGroupId = request?.ClientGroupId;
        client.IsDisabled = request?.IsDisabled ?? false;
        client.Comments = request?.Comments;
        client.DeviceIconKey = deviceIconKey;
        client.IsFavorite = request?.IsFavorite ?? false;
        client.UpdatedAtUtc = DateTimeOffset.UtcNow;

        clientRepository.Update(client);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action updates a client's editable details as an RFC 7396 merge patch: an absent
    ///     field is unchanged, a JSON <c>null</c> clears a nullable field, and any other value
    ///     replaces it. Address fields are not recognized and are ignored. Errors are as for
    ///     <see cref="Replace" />. Requires <c>clients:write</c>.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ClientPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        DnsClientEntity client = await clientRepository.GetByIdAsync(id, stoppingToken);
        if (client is null)
            throw new NotFoundException("Client", id.ToString());

        if (patch.TryGetMergePatchField("displayName", out JsonElement displayName))
            client.DisplayName =
                displayName.ValueKind == JsonValueKind.Null ? null : displayName.GetString();

        if (patch.TryGetMergePatchField("clientGroupId", out JsonElement clientGroupIdElement))
        {
            if (clientGroupIdElement.ValueKind == JsonValueKind.Null)
            {
                client.ClientGroupId = null;
            }
            else
            {
                Guid newClientGroupId = clientGroupIdElement.GetGuid();
                if (
                    await clientGroupRepository.GetByIdAsync(newClientGroupId, stoppingToken)
                    is null
                )
                    throw new ValidationException(
                        $"'clientGroupId' does not name an existing client group ('{newClientGroupId}')."
                    );

                client.ClientGroupId = newClientGroupId;
            }
        }

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            client.IsDisabled = isDisabled.GetBoolean();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            client.Comments =
                comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        if (patch.TryGetMergePatchField("deviceIconKey", out JsonElement deviceIconKey))
        {
            if (deviceIconKey.ValueKind is not (JsonValueKind.Null or JsonValueKind.String))
                throw new ValidationException("'deviceIconKey' must be a string or null.");

            client.DeviceIconKey = ParseDeviceIconKey(
                deviceIconKey.ValueKind == JsonValueKind.Null ? null : deviceIconKey.GetString()
            );
        }

        if (patch.TryGetMergePatchField("isFavorite", out JsonElement isFavorite))
        {
            if (isFavorite.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ValidationException("'isFavorite' must be a boolean.");

            client.IsFavorite = isFavorite.GetBoolean();
        }

        client.UpdatedAtUtc = DateTimeOffset.UtcNow;
        clientRepository.Update(client);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action deletes a known client with its addresses, schedules, direct filter-list
    ///     assignments and the rewrites scoped to it (its Safe Search overrides). Leases and DNS
    ///     records the device holds are not affected; the registry only describes devices. Scoped
    ///     rewrites are not cluster-synced. Returns 404 for an unknown id. Requires
    ///     <c>clients:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clients:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DnsClientEntity client = await clientRepository.GetByIdAsync(id, stoppingToken);
        if (client is null)
            throw new NotFoundException("Client", id.ToString());

        // The addresses go in the same unit of work. An orphaned address would keep its identifier
        // reserved by the unique (ClientIdentifier, ProtocolVersion) index while lookups through
        // the device table found nothing, so re-registering it, by hand or through DHCP, would fail
        // on a duplicate key.
        foreach (
            DnsClientAddressEntity address in await clientAddressRepository.ListByClientIdAsync(
                client.Id,
                stoppingToken
            )
        )
            clientAddressRepository.Remove(address);

        foreach (
            DnsClientScheduleEntity schedule in await scheduleRepository.ListByClientIdAsync(
                client.Id,
                stoppingToken
            )
        )
            scheduleRepository.Remove(schedule);

        foreach (
            DnsClientListAssignmentEntity assignment in await listAssignmentRepository.ListByClientIdAsync(
                client.Id,
                stoppingToken
            )
        )
            listAssignmentRepository.Remove(assignment);

        // A scoped rewrite is deleted, never widened to global.
        foreach (
            DnsRewriteEntity rewrite in await rewriteRepository.ListAsync(
                rewrite => rewrite.ClientId == client.Id,
                stoppingToken
            )
        )
            rewriteRepository.Remove(rewrite);

        clientRepository.Remove(client);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action pauses one client now: all its DNS queries are blocked, whatever schedules,
    ///     filter lists or rewrites would apply, for
    ///     <see cref="InstantPauseRequest.DurationMinutes" /> minutes. Calling it during a pause
    ///     replaces the pause rather than extending it. Returns 400 for a duration that is not
    ///     positive and 404 for an unknown id. Requires <c>clients:write</c>.
    /// </summary>
    [HttpPost("{id:guid}/pause")]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(typeof(InstantPauseRequest), typeof(ClientPauseRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientDetailExample))]
    public async Task<ActionResult<ClientDetail>> Pause(
        Guid id,
        InstantPauseRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsClientEntity client = await clientRepository.GetByIdAsync(id, stoppingToken);
        if (client is null)
            throw new NotFoundException("Client", id.ToString());

        if (request is null || request.DurationMinutes <= 0)
            throw new ValidationException(
                "'durationMinutes' must be a positive number of minutes."
            );

        client.PausedUntilUtc = DateTimeOffset.UtcNow.AddMinutes(request.DurationMinutes);
        client.UpdatedAtUtc = DateTimeOffset.UtcNow;

        clientRepository.Update(client);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return Ok(await ToDetailAsync(client, stoppingToken));
    }

    /// <summary>
    ///     This action ends a client's pause now by clearing
    ///     <see cref="DnsClientEntity.PausedUntilUtc" />. Resuming a client that is not paused is
    ///     not an error. Returns 404 for an unknown id. Requires <c>clients:write</c>.
    /// </summary>
    [HttpPost("{id:guid}/resume")]
    [RequirePermission("clients:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientDetailExample))]
    public async Task<ActionResult<ClientDetail>> Resume(Guid id, CancellationToken stoppingToken)
    {
        DnsClientEntity client = await clientRepository.GetByIdAsync(id, stoppingToken);
        if (client is null)
            throw new NotFoundException("Client", id.ToString());

        client.PausedUntilUtc = null;
        client.UpdatedAtUtc = DateTimeOffset.UtcNow;

        clientRepository.Update(client);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return Ok(await ToDetailAsync(client, stoppingToken));
    }

    /// <summary>
    ///     This method converts a client entity to its API representation, loading its current
    ///     addresses (never cached, since addresses change independently of the client).
    /// </summary>
    private async Task<ClientDetail> ToDetailAsync(
        DnsClientEntity client,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IReadOnlyList<DnsClientAddressEntity> addresses =
            await clientAddressRepository.ListByClientIdAsync(client.Id, stoppingToken);

        // A device's last-seen time is that of its most recently seen address.
        DateTimeOffset? lastSeenUtc = addresses.Max(address => address.LastSeenUtc);

        // Each address's vendor is looked up once. The device's vendor is the first one found; it
        // counts as randomized only when every MAC it embeds is randomized, so a device with one
        // real and one private MAC still reports its real vendor.
        ClientAddressDetail[] addressDetails =
        [
            .. addresses.Select(address => ToAddressDetail(address, vendorLookup)),
        ];
        string vendor = addressDetails
            .Select(address => address.Vendor)
            .FirstOrDefault(candidate => candidate is not null);
        ClientAddressDetail[] withMac =
        [
            .. addressDetails.Where(address => address.MacAddress is not null),
        ];
        bool isRandomized =
            vendor is null
            && withMac.Length > 0
            && withMac.All(address => address.IsRandomizedMacAddress);

        return new ClientDetail(
            client.Id,
            addressDetails,
            client.DisplayName,
            client.ClientGroupId,
            client.IsDisabled,
            client.Comments,
            client.Origin,
            client.CreatedAtUtc,
            client.UpdatedAtUtc,
            client.PausedUntilUtc,
            client.PausedUntilUtc is { } pausedUntilUtc && pausedUntilUtc > now,
            lastSeenUtc,
            presenceOptions.CurrentValue.IsOnline(lastSeenUtc, now),
            client.DeviceIconKey,
            MacVendorDeviceIconSuggester.Suggest(vendor),
            client.IsFavorite,
            vendor,
            isRandomized
        );
    }

    /// <summary>
    ///     This method converts one address entity to its API representation, including the vendor
    ///     of its embedded MAC address. <see cref="ClientAddressesController" /> uses it too.
    /// </summary>
    /// <param name="address">The address to convert.</param>
    /// <param name="vendorLookup">The lookup the address's vendor fields are computed through.</param>
    internal static ClientAddressDetail ToAddressDetail(
        DnsClientAddressEntity address,
        IMacVendorLookupService vendorLookup
    )
    {
        MacVendorLookupResult vendor = vendorLookup.LookupClientIdentifier(
            address.ClientIdentifier,
            address.ProtocolVersion
        );

        return new ClientAddressDetail(
            address.Id,
            address.ClientId,
            address.ClientIdentifier,
            address.ProtocolVersion,
            address.Address,
            address.FirstSeenUtc,
            address.LastSeenUtc,
            address.CreatedAtUtc,
            vendor.MacAddress,
            vendor.Vendor,
            vendor.IsRandomized
        );
    }

    /// <summary>
    ///     This method checks a device icon name against <see cref="DnsClientDeviceIconEnum" />,
    ///     ignoring case. A blank value clears the choice. Anything else, including a numeric
    ///     string a plain enum parse would accept, returns 400 rather than being turned into some
    ///     other icon.
    /// </summary>
    /// <param name="value">The requested icon name, or <see langword="null" />.</param>
    /// <returns>The parsed icon, or <see langword="null" /> for no explicit choice.</returns>
    internal static DnsClientDeviceIconEnum? ParseDeviceIconKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        foreach (DnsClientDeviceIconEnum icon in Enum.GetValues<DnsClientDeviceIconEnum>())
        {
            if (string.Equals(icon.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
                return icon;
        }

        throw new ValidationException(
            $"'deviceIconKey' must be one of: {string.Join(", ", Enum.GetNames<DnsClientDeviceIconEnum>())}."
        );
    }
}
