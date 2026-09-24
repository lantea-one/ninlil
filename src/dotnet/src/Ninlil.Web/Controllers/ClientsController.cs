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
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this server's own "known client" registry
///     (<see cref="DnsClientEntity" />) -- devices this server has either been told about
///     directly, through this controller, or has auto-discovered on its own the first time a DHCP
///     lease was granted for a previously-unseen client identifier (see
///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpClientRegistryAutoPopulationService</c>). This
///     controller itself has no dependency on DHCP being present or enabled at all -- every action
///     below works standalone against <see cref="IDnsClientRepository" /> regardless. Every action
///     requires the <c>clients:read</c> or <c>clients:write</c> permission.
///     <para>
///         <b>An address, once created, is immutable.</b> Together
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientAddressEntity.ClientIdentifier" />/
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientAddressEntity.ProtocolVersion" />
///         are one address row's own entire identity: it is exactly the pair
///         <c>IDnsClientRepository.GetByClientIdentifierAsync</c> looks a device up by, and the
///         pair the DHCP-driven auto-population feature above uses to decide "have I seen this
///         device before." Letting either half of this pair change would silently orphan that
///         lookup -- a later DHCP lease grant for the device's own original identifier would
///         simply never find this row again and would spawn a second, duplicate device instead --
///         and would let an operator produce a row that is internally inconsistent in a way no
///         real protocol ever produces (a DUID-shaped string tagged <c>IPv4</c>, for example,
///         since a client identifier's own shape is dictated entirely by which protocol issued
///         it). <see cref="ClientWriteRequest" /> (used for <see cref="Replace" />/
///         <see cref="Patch" />) therefore carries no address field at all; to actually change a
///         device's own address, remove it (<see cref="ClientAddressesController.Delete" />) and
///         add a new one (<see cref="ClientAddressesController.Create" />), mirroring the exact
///         "immutable field, delete and recreate" precedent <c>TsigKeysController</c> already
///         establishes for a TSIG key's own shared secret. <see cref="Create" /> still accepts one
///         initial address directly, since a device with no address at all could never be resolved
///         by anything -- see <see cref="ClientCreateRequest" />'s own doc comment.
///     </para>
///     <para>
///         <b>Vendor and suggested icon are computed at read time, never stored.</b> Every
///         response resolves each address's embedded MAC address against the IEEE registries
///         (<see cref="IMacVendorLookupService" />) and derives a suggested device icon from the
///         result, so both follow a registry refresh automatically. Only an operator's explicit
///         <see cref="DnsClientEntity.DeviceIconKey" /> choice and <see cref="DnsClientEntity.IsFavorite" />
///         star are persisted.
///     </para>
/// </summary>
/// <param name="clientRepository">The repository this controller reads and writes known clients through.</param>
/// <param name="clientAddressRepository">The repository used to create a new client's own initial address, and to render every device's own address list.</param>
/// <param name="clientGroupRepository">The repository used to confirm a client's own optional <see cref="ClientCreateRequest.ClientGroupId" /> names a real group.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="presenceOptions">The live presence options, whose online window decides each device's reported <see cref="ClientDetail.IsOnline" />.</param>
/// <param name="vendorLookup">The MAC vendor lookup each response's vendor fields are computed through.</param>
[ApiController]
[Route("api/clients")]
public class ClientsController(
    IDnsClientRepository clientRepository,
    IDnsClientAddressRepository clientAddressRepository,
    IDnsClientGroupRepository clientGroupRepository,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<ClientPresenceOptions> presenceOptions,
    IMacVendorLookupService vendorLookup
) : ControllerBase
{
    /// <summary>
    ///     This action lists every known client this server is tracking, optionally narrowed to
    ///     favorites or non-favorites (<c>isFavorite</c>) and to devices currently online or
    ///     offline (<c>presence</c>) and to devices registered since a given instant
    ///     (<c>createdSinceUtc</c>), in the requested order (<c>sort</c>, creation order by
    ///     default). Presence is evaluated against the live online window at the moment of the
    ///     request -- the same cutoff each returned device's own <see cref="ClientDetail.IsOnline" />
    ///     is computed from -- and every filter and ordering is pushed down to the storage engine.
    /// </summary>
    /// <param name="pagination">The page to return.</param>
    /// <param name="isFavorite">When set, only clients whose favorite star matches this value are listed.</param>
    /// <param name="presence">When set, only clients currently online, or currently offline, are listed.</param>
    /// <param name="createdSinceUtc">When set, only clients registered at or after this instant are listed.</param>
    /// <param name="sort">The order to list clients in; creation order when omitted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpGet]
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

        // "Online" means seen at or after now minus the window, exactly the inclusive boundary
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

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated WHERE/ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every client into memory first.
        IReadOnlyList<DnsClientEntity> clients = await clientRepository.SearchAsync(
            criteria,
            skip,
            take,
            stoppingToken
        );
        int totalCount = await clientRepository.CountAsync(criteria, stoppingToken);

        // Each client's own address set is fetched in parallel -- one query per client (this
        // page's own bounded size, never the whole table), acceptable at this project's own
        // homelab/small-org target scale.
        ClientDetail[] details = await Task.WhenAll(
            clients.Select(client => ToDetailAsync(client, stoppingToken))
        );

        return Ok(new PagedResult<ClientDetail>(details, skip, take, totalCount));
    }

    /// <summary>
    ///     This action reports the registry's headline counts -- total, online, offline, new, and
    ///     favorites -- each a pushed-down count over the same filters <see cref="List" /> applies,
    ///     so a count always agrees with the filtered list it summarizes.
    /// </summary>
    /// <param name="newSinceUtc">
    ///     The instant "new" counts from. A caller showing "new today" passes its own local
    ///     midnight, since this server cannot know the viewer's time zone; when omitted, the last
    ///     24 hours are counted. A value in the future is rejected with a <c>400</c>.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpGet("summary")]
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

        // Sequential rather than parallel: every count shares this request's one scoped
        // repository, and a DbContext never allows two concurrent operations.
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

        // Offline is the exact complement of online for the same cutoff, so it is derived rather
        // than counted separately -- a device written between two count queries can never make
        // the two disagree with the total.
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
    ///     This action looks up a single known client by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action registers a new known client device together with its first address.
    ///     Rejected with <c>409 Conflict</c> when a device already exists for the same
    ///     <see cref="ClientCreateRequest.ClientIdentifier" />/<see cref="ClientCreateRequest.ProtocolVersion" />
    ///     pair -- the same real, storage-level uniqueness
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientAddressEntity" />'s own
    ///     composite index enforces, translated here via a pre-check against
    ///     <see cref="IDnsClientRepository.GetByClientIdentifierAsync" />, mirroring exactly how
    ///     every other controller in this project translates a storage-level uniqueness violation
    ///     into a real <c>ConflictException</c> (see <see cref="TsigKeysController.Create" />'s own
    ///     <c>FindByNameAsync</c> pre-check for the closest precedent).
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
    ///     This action fully replaces an existing known client's own editable metadata -- see
    ///     this class's own doc comment for why a device's own address(es) can never be changed
    ///     this way.
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
    ///     This action partially updates an existing known client's own editable metadata via RFC
    ///     7396 JSON Merge Patch: a field absent from the patch document is left untouched, and
    ///     any other present field overwrites it. As with <see cref="Replace" />, this action
    ///     never recognizes an address field -- a patch document naming one is simply ignored, the
    ///     same way <c>TsigKeysController.Patch</c> never looks for a <c>secret</c> field at all.
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
    ///     This action permanently deletes a known client. This has no effect on any lease/record
    ///     the device may still hold -- this registry is purely descriptive metadata about a
    ///     device, never the source of truth for its own address assignment or DNS content.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clients:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DnsClientEntity client = await clientRepository.GetByIdAsync(id, stoppingToken);
        if (client is null)
            throw new NotFoundException("Client", id.ToString());

        // Remove every address the device answers to alongside the device itself, in the same
        // unit of work. An orphaned address row would keep its identifier reserved by the unique
        // (ClientIdentifier, ProtocolVersion) index while every lookup through the device table
        // came back empty -- so re-registering that identifier, manually or via DHCP
        // auto-population, would fail on a duplicate key forever.
        foreach (
            DnsClientAddressEntity address in await clientAddressRepository.ListByClientIdAsync(
                client.Id,
                stoppingToken
            )
        )
            clientAddressRepository.Remove(address);

        clientRepository.Remove(client);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action starts, or replaces, an instant pause on a single known client --
    ///     blocking every one of its own DNS queries outright, regardless of any schedule,
    ///     filter-list, or rewrite that would otherwise apply, until
    ///     <see cref="InstantPauseRequest.DurationMinutes" /> minutes from now. Calling this again
    ///     while a pause is already active simply replaces it with a fresh one -- there is no
    ///     "extend" semantic, matching how every other write action in this controller always
    ///     fully overwrites the field(s) it targets.
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
    ///     This action ends a client's own instant pause immediately, clearing
    ///     <see cref="DnsClientEntity.PausedUntilUtc" /> back to <see langword="null" /> regardless
    ///     of how much time was left on it. A no-op, not an error, when the client was not
    ///     currently paused at all.
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
    ///     This method converts a known client entity into its REST representation, fetching its
    ///     own current address set fresh from <see cref="IDnsClientAddressRepository" /> -- never
    ///     cached, since an address can be added or removed independently of any client-level
    ///     write this controller's own actions perform.
    /// </summary>
    private async Task<ClientDetail> ToDetailAsync(
        DnsClientEntity client,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IReadOnlyList<DnsClientAddressEntity> addresses =
            await clientAddressRepository.ListByClientIdAsync(client.Id, stoppingToken);

        // A device's own presence is its most recently seen address.
        DateTimeOffset? lastSeenUtc = addresses.Max(address => address.LastSeenUtc);

        // Resolve every address's vendor once. The device's vendor is the first address that
        // yields one; it counts as randomized only when every MAC it embeds is randomized, so a
        // device with one real and one private MAC still reports its real vendor.
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
    ///     This method converts a single client address entity into its REST representation,
    ///     including the vendor its embedded MAC address resolves to -- shared with
    ///     <see cref="ClientAddressesController" />'s own identical conversion need.
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
    ///     This method validates a device icon named in a write request against the fixed
    ///     <see cref="DnsClientDeviceIconEnum" /> catalog, by its exact wire name (case
    ///     insensitive). A blank value clears the choice. Anything else -- including a numeric
    ///     string, which a plain enum parse would otherwise accept -- is rejected rather than
    ///     silently coerced to some other icon.
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
