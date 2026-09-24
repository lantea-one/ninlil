using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Statistics;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpScopes;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this server's own DHCP scopes -- the top-level resource; a
///     scope's own static reservations and custom options are managed separately, through the
///     nested <see cref="DhcpReservationsController" /> (<c>/api/dhcp-scopes/{scopeId}/reservations</c>)
///     and <see cref="DhcpScopeCustomOptionsController" /> (<c>/api/dhcp-scopes/{scopeId}/custom-options</c>)
///     -- the same "one parent, several nested child collections" shape
///     <see cref="ClientGroupsController" /> already establishes for a client group's own networks
///     and list assignments. Every action requires the <c>dhcpscopes:read</c> or
///     <c>dhcpscopes:write</c> permission, reused for both nested controllers too.
/// </summary>
/// <param name="scopeRepository">The repository this controller reads and writes scopes through.</param>
/// <param name="reservationRepository">The repository used to remove a deleted scope's own reservations -- see <see cref="Delete" />.</param>
/// <param name="customOptionRepository">The repository used to remove a deleted scope's own custom options -- see <see cref="Delete" />.</param>
/// <param name="zoneRepository">
///     The repository used to confirm a scope's own <see cref="DhcpScopeWriteRequest.DnsZoneId" />
///     names a real zone -- existence only, never zone-type eligibility, mirroring
///     <c>ZonesController</c>'s own established "enforced at the reconciliation-service level,
///     not the REST layer" precedent for <c>AutoManageCaaRecords</c>/<c>AutoManagePtrRecords</c>.
/// </param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="statisticsService">
///     The service used to compute a scope's own real, current utilization -- see
///     <see cref="Statistics" />.
/// </param>
[ApiController]
[Route("api/dhcp-scopes")]
public class DhcpScopesController(
    IDhcpScopeRepository scopeRepository,
    IDhcpReservationRepository reservationRepository,
    IDhcpScopeCustomOptionRepository customOptionRepository,
    IZoneRepository zoneRepository,
    IUnitOfWork unitOfWork,
    IDhcpScopeStatisticsService statisticsService
) : ControllerBase
{
    /// <summary>
    ///     This action lists every DHCP scope this server manages.
    /// </summary>
    [HttpGet]
    [RequirePermission("dhcpscopes:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpScopeDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<DhcpScopeDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DhcpScopeEntity> scopes = await scopeRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await scopeRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<DhcpScopeDetail>([.. scopes.Select(ToDetail)], skip, take, totalCount)
        );
    }

    /// <summary>
    ///     This action looks up a single DHCP scope by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("dhcpscopes:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpScopeDetailExample))]
    public async Task<ActionResult<DhcpScopeDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(id, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", id.ToString());

        return Ok(ToDetail(scope));
    }

    /// <summary>
    ///     This action reports a single DHCP scope's own real, current utilization -- total
    ///     addresses/prefixes in the scope's own configured range, how many are currently leased or
    ///     tentatively offered, how many the scope's own static reservations account for, and how
    ///     many remain genuinely available -- computed fresh from this scope's own configuration
    ///     and its real, currently-recorded lease/reservation rows on every call, never a cached or
    ///     approximate figure. See <see cref="IDhcpScopeStatisticsService" />'s own doc comment for
    ///     exactly how each field is derived.
    /// </summary>
    [HttpGet("{id:guid}/statistics")]
    [RequirePermission("dhcpscopes:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpScopeStatisticsDetailExample))]
    public async Task<ActionResult<DhcpScopeStatisticsDetail>> Statistics(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(id, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", id.ToString());

        DhcpScopeStatistics statistics = await statisticsService.ComputeAsync(scope, stoppingToken);

        return Ok(
            new DhcpScopeStatisticsDetail(
                scope.Id,
                scope.Name,
                scope.ProtocolVersion,
                scope.Kind,
                statistics.TotalSlots.ToString(),
                statistics.LeasedCount,
                statistics.OfferedCount,
                statistics.ReservedCount,
                statistics.AvailableSlots.ToString()
            )
        );
    }

    /// <summary>
    ///     This action creates a new DHCP scope.
    /// </summary>
    [HttpPost]
    [RequirePermission("dhcpscopes:write")]
    [SwaggerRequestExample(typeof(DhcpScopeWriteRequest), typeof(DhcpScopeWriteRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(DhcpScopeDetailExample))]
    public async Task<ActionResult<DhcpScopeDetail>> Create(
        DhcpScopeWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        if (await scopeRepository.FindByNameAsync(request.Name, stoppingToken) is not null)
            throw new ConflictException($"A DHCP scope named '{request.Name}' already exists.");

        if (
            request.DnsZoneId is { } dnsZoneId
            && await zoneRepository.GetByIdAsync(dnsZoneId, stoppingToken) is null
        )
            throw new ValidationException(
                $"'dnsZoneId' does not name an existing zone ('{dnsZoneId}')."
            );

        DhcpScopeEntity scope = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ProtocolVersion = request.ProtocolVersion,
            Kind = request.Kind,
            RangeStartAddress = request.RangeStartAddress,
            RangeEndAddress = request.RangeEndAddress,
            SubnetPrefixLength = request.SubnetPrefixLength,
            DefaultLeaseDuration = request.DefaultLeaseDuration,
            MaxLeaseDuration = request.MaxLeaseDuration,
            IsDisabled = request.IsDisabled,
            GatewayAddresses = request.GatewayAddresses ?? [],
            DnsServerAddresses = request.DnsServerAddresses ?? [],
            Comments = request.Comments,
            DnsZoneId = request.DnsZoneId,
            BootFileName = request.BootFileName,
            NextServerAddress = request.NextServerAddress,
            DelegatedPrefixLength = request.DelegatedPrefixLength,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await scopeRepository.AddAsync(scope, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(nameof(Get), new { id = scope.Id }, ToDetail(scope));
    }

    /// <summary>
    ///     This action fully replaces an existing DHCP scope's own fields.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("dhcpscopes:write")]
    [SwaggerRequestExample(typeof(DhcpScopeWriteRequest), typeof(DhcpScopeWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        DhcpScopeWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(id, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", id.ToString());

        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        DhcpScopeEntity existingByName = await scopeRepository.FindByNameAsync(
            request.Name,
            stoppingToken
        );
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException($"A DHCP scope named '{request.Name}' already exists.");

        if (
            request.DnsZoneId is { } dnsZoneId
            && await zoneRepository.GetByIdAsync(dnsZoneId, stoppingToken) is null
        )
            throw new ValidationException(
                $"'dnsZoneId' does not name an existing zone ('{dnsZoneId}')."
            );

        scope.Name = request.Name;
        scope.ProtocolVersion = request.ProtocolVersion;
        scope.Kind = request.Kind;
        scope.RangeStartAddress = request.RangeStartAddress;
        scope.RangeEndAddress = request.RangeEndAddress;
        scope.SubnetPrefixLength = request.SubnetPrefixLength;
        scope.DefaultLeaseDuration = request.DefaultLeaseDuration;
        scope.MaxLeaseDuration = request.MaxLeaseDuration;
        scope.IsDisabled = request.IsDisabled;
        scope.GatewayAddresses = request.GatewayAddresses ?? [];
        scope.DnsServerAddresses = request.DnsServerAddresses ?? [];
        scope.Comments = request.Comments;
        scope.DnsZoneId = request.DnsZoneId;
        scope.BootFileName = request.BootFileName;
        scope.NextServerAddress = request.NextServerAddress;
        scope.DelegatedPrefixLength = request.DelegatedPrefixLength;
        scope.UpdatedAtUtc = DateTimeOffset.UtcNow;

        scopeRepository.Update(scope);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action partially updates an existing DHCP scope via RFC 7396 JSON Merge Patch: a
    ///     field absent from the patch document is left untouched, and any other present field
    ///     overwrites it.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("dhcpscopes:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DhcpScopePatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(id, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement name))
        {
            string newName = name.GetString();
            if (string.IsNullOrWhiteSpace(newName))
                throw new ValidationException("'name' must not be empty.");

            DhcpScopeEntity existingByName = await scopeRepository.FindByNameAsync(
                newName,
                stoppingToken
            );
            if (existingByName is not null && existingByName.Id != id)
                throw new ConflictException($"A DHCP scope named '{newName}' already exists.");

            scope.Name = newName;
        }

        if (patch.TryGetMergePatchField("rangeStartAddress", out JsonElement rangeStartAddress))
        {
            string newStart = rangeStartAddress.GetString();
            if (!TryParseAddress(newStart, scope.ProtocolVersion, out string startError))
                throw new ValidationException(startError);

            scope.RangeStartAddress = newStart;
        }

        if (patch.TryGetMergePatchField("rangeEndAddress", out JsonElement rangeEndAddress))
        {
            string newEnd = rangeEndAddress.GetString();
            if (!TryParseAddress(newEnd, scope.ProtocolVersion, out string endError))
                throw new ValidationException(endError);

            scope.RangeEndAddress = newEnd;
        }

        if (patch.TryGetMergePatchField("subnetPrefixLength", out JsonElement subnetPrefixLength))
        {
            int newPrefixLength = subnetPrefixLength.GetInt32();
            if (!IsValidPrefixLength(newPrefixLength, scope.ProtocolVersion))
                throw new ValidationException(
                    $"'subnetPrefixLength' must be between 0 and "
                        + $"{MaxPrefixLength(scope.ProtocolVersion)} for {scope.ProtocolVersion}."
                );

            scope.SubnetPrefixLength = newPrefixLength;
        }

        if (
            patch.TryGetMergePatchField(
                "defaultLeaseDuration",
                out JsonElement defaultLeaseDuration
            )
        )
            scope.DefaultLeaseDuration = TimeSpan.Parse(defaultLeaseDuration.GetString());

        if (patch.TryGetMergePatchField("maxLeaseDuration", out JsonElement maxLeaseDuration))
            scope.MaxLeaseDuration = TimeSpan.Parse(maxLeaseDuration.GetString());

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            scope.IsDisabled = isDisabled.GetBoolean();

        if (patch.TryGetMergePatchField("gatewayAddresses", out JsonElement gatewayAddresses))
            scope.GatewayAddresses = ParseAddressList(gatewayAddresses);

        if (patch.TryGetMergePatchField("dnsServerAddresses", out JsonElement dnsServerAddresses))
            scope.DnsServerAddresses = ParseAddressList(dnsServerAddresses);

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            scope.Comments = comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        if (patch.TryGetMergePatchField("dnsZoneId", out JsonElement dnsZoneId))
        {
            if (dnsZoneId.ValueKind == JsonValueKind.Null)
            {
                scope.DnsZoneId = null;
            }
            else
            {
                Guid newDnsZoneId = dnsZoneId.GetGuid();
                if (await zoneRepository.GetByIdAsync(newDnsZoneId, stoppingToken) is null)
                    throw new ValidationException(
                        $"'dnsZoneId' does not name an existing zone ('{newDnsZoneId}')."
                    );

                scope.DnsZoneId = newDnsZoneId;
            }
        }

        if (patch.TryGetMergePatchField("bootFileName", out JsonElement bootFileName))
        {
            string newBootFileName =
                bootFileName.ValueKind == JsonValueKind.Null ? null : bootFileName.GetString();

            if (!TryValidateBootFileName(newBootFileName, out string bootFileNameError))
                throw new ValidationException(bootFileNameError);

            scope.BootFileName = newBootFileName;
        }

        if (patch.TryGetMergePatchField("nextServerAddress", out JsonElement nextServerAddress))
        {
            string newNextServerAddress =
                nextServerAddress.ValueKind == JsonValueKind.Null
                    ? null
                    : nextServerAddress.GetString();

            if (!TryValidateNextServerAddress(newNextServerAddress, out string addressError))
                throw new ValidationException(addressError);

            scope.NextServerAddress = newNextServerAddress;
        }

        if (patch.TryGetMergePatchField("kind", out JsonElement kind))
            scope.Kind = JsonSerializer.Deserialize<DhcpScopeKindEnum>(kind.GetRawText());

        if (
            patch.TryGetMergePatchField(
                "delegatedPrefixLength",
                out JsonElement delegatedPrefixLength
            )
        )
            scope.DelegatedPrefixLength =
                delegatedPrefixLength.ValueKind == JsonValueKind.Null
                    ? null
                    : delegatedPrefixLength.GetInt32();

        if (scope.DefaultLeaseDuration > scope.MaxLeaseDuration)
            throw new ValidationException(
                "'defaultLeaseDuration' must not be greater than 'maxLeaseDuration'."
            );

        if (
            !TryValidateKindAndDelegatedPrefixLength(
                scope.Kind,
                scope.ProtocolVersion,
                scope.DelegatedPrefixLength,
                out string kindError
            )
        )
            throw new ValidationException(kindError);

        scope.UpdatedAtUtc = DateTimeOffset.UtcNow;
        scopeRepository.Update(scope);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action permanently deletes a DHCP scope, every reservation it contains, and every
    ///     custom option it contains -- removed explicitly, rather than relying on the relational
    ///     backend's own cascade delete, since MongoDB has no equivalent mechanism at all, the same
    ///     reasoning <see cref="ClientGroupsController.Delete" /> already applies to a deleted
    ///     client group's own dependent rows. Any lease already recorded against this scope (keyed
    ///     by its own <c>ScopeName</c>, not this scope's identifier) is left untouched -- a lease is
    ///     historical/operational data, not configuration this deletion owns, the same "never
    ///     touch operational telemetry on a configuration delete" posture
    ///     <c>Server.Backup.BackupEntityCatalog</c> already establishes. A tag rule that names this
    ///     scope's own identifier (<c>DhcpTagRuleEntity.ScopeId</c>) is also left untouched -- it
    ///     simply becomes permanently ineligible to match any future client, exactly like a rule
    ///     naming a scope that was never created in the first place.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("dhcpscopes:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(id, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", id.ToString());

        IReadOnlyList<DhcpReservationEntity> reservations = await reservationRepository.ListAsync(
            reservation => reservation.ScopeId == id,
            stoppingToken
        );
        foreach (DhcpReservationEntity reservation in reservations)
            reservationRepository.Remove(reservation);

        IReadOnlyList<DhcpScopeCustomOptionEntity> customOptions =
            await customOptionRepository.ListAsync(
                customOption => customOption.ScopeId == id,
                stoppingToken
            );
        foreach (DhcpScopeCustomOptionEntity customOption in customOptions)
            customOptionRepository.Remove(customOption);

        scopeRepository.Remove(scope);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method returns the largest valid CIDR prefix length for a protocol version -- 32
    ///     for IPv4, 128 for IPv6.
    /// </summary>
    private static int MaxPrefixLength(InternetProtocolVersionEnum protocolVersion) =>
        protocolVersion == InternetProtocolVersionEnum.IPv6 ? 128 : 32;

    /// <summary>
    ///     This method checks whether a prefix length is valid for a protocol version.
    /// </summary>
    private static bool IsValidPrefixLength(
        int prefixLength,
        InternetProtocolVersionEnum protocolVersion
    ) => prefixLength >= 0 && prefixLength <= MaxPrefixLength(protocolVersion);

    /// <summary>
    ///     This method validates that an address string parses and matches the address family
    ///     implied by <paramref name="protocolVersion" />.
    /// </summary>
    private static bool TryParseAddress(
        string address,
        InternetProtocolVersionEnum protocolVersion,
        out string error
    )
    {
        error = null;

        if (
            !IPAddress.TryParse(address, out IPAddress parsed)
            || (protocolVersion == InternetProtocolVersionEnum.IPv6)
                != (parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        )
        {
            error = $"'{address}' is not a valid {protocolVersion} address.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method parses a JSON array of address strings from a merge-patch field into a
    ///     concrete list, tolerating an absent/empty array as "no addresses configured."
    /// </summary>
    private static List<string> ParseAddressList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException("Expected a JSON array of address strings.");

        List<string> addresses = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            string address = item.GetString();
            if (!IPAddress.TryParse(address, out _))
                throw new ValidationException($"'{address}' is not a valid IP address.");

            addresses.Add(address);
        }

        return addresses;
    }

    /// <summary>
    ///     This method validates every field of a scope write request up front, so a
    ///     <c>POST</c>/<c>PUT</c> either fully succeeds or is rejected outright with no partial
    ///     effect.
    /// </summary>
    private static bool TryValidate(DhcpScopeWriteRequest request, out string error)
    {
        error = null;

        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            error = "'name' is required.";
            return false;
        }

        if (!TryParseAddress(request.RangeStartAddress, request.ProtocolVersion, out error))
            return false;

        if (!TryParseAddress(request.RangeEndAddress, request.ProtocolVersion, out error))
            return false;

        if (!IsValidPrefixLength(request.SubnetPrefixLength, request.ProtocolVersion))
        {
            error =
                $"'subnetPrefixLength' must be between 0 and "
                + $"{MaxPrefixLength(request.ProtocolVersion)} for {request.ProtocolVersion}.";
            return false;
        }

        if (request.DefaultLeaseDuration <= TimeSpan.Zero)
        {
            error = "'defaultLeaseDuration' must be a positive duration.";
            return false;
        }

        if (request.MaxLeaseDuration <= TimeSpan.Zero)
        {
            error = "'maxLeaseDuration' must be a positive duration.";
            return false;
        }

        if (request.DefaultLeaseDuration > request.MaxLeaseDuration)
        {
            error = "'defaultLeaseDuration' must not be greater than 'maxLeaseDuration'.";
            return false;
        }

        foreach (string address in request.GatewayAddresses ?? [])
        {
            if (IPAddress.TryParse(address, out _))
                continue;

            error = $"'{address}' is not a valid gateway address.";
            return false;
        }

        foreach (string address in request.DnsServerAddresses ?? [])
        {
            if (IPAddress.TryParse(address, out _))
                continue;

            error = $"'{address}' is not a valid DNS server address.";
            return false;
        }

        if (!TryValidateBootFileName(request.BootFileName, out error))
            return false;

        if (!TryValidateNextServerAddress(request.NextServerAddress, out error))
            return false;

        if (
            !TryValidateKindAndDelegatedPrefixLength(
                request.Kind,
                request.ProtocolVersion,
                request.DelegatedPrefixLength,
                out error
            )
        )
            return false;

        return true;
    }

    /// <summary>
    ///     This method validates the combination of <see cref="DhcpScopeKindEnum" /> and
    ///     <c>DelegatedPrefixLength</c> a scope write request carries: a
    ///     <see cref="DhcpScopeKindEnum.PrefixDelegation" /> scope is IPv6-only (RFC 8415 Prefix
    ///     Delegation has no DHCPv4 analogue at all -- see <c>DhcpScopeKindEnum</c>'s own doc
    ///     comment) and always requires a real, in-range delegated prefix length; an
    ///     <see cref="DhcpScopeKindEnum.AddressAssignment" /> scope -- every DHCPv4 scope, and an
    ///     ordinary stateful DHCPv6 scope alike -- has no delegated prefix length of its own to
    ///     speak of, so one being set at all is rejected outright rather than silently ignored.
    /// </summary>
    private static bool TryValidateKindAndDelegatedPrefixLength(
        DhcpScopeKindEnum kind,
        InternetProtocolVersionEnum protocolVersion,
        int? delegatedPrefixLength,
        out string error
    )
    {
        error = null;

        if (kind == DhcpScopeKindEnum.AddressAssignment)
        {
            if (delegatedPrefixLength is not null)
            {
                error = "'delegatedPrefixLength' must not be set for an 'AddressAssignment' scope.";
                return false;
            }

            return true;
        }

        // Kind == PrefixDelegation from here on.
        if (protocolVersion != InternetProtocolVersionEnum.IPv6)
        {
            error = "'kind' can only be 'PrefixDelegation' for an IPv6 scope.";
            return false;
        }

        if (delegatedPrefixLength is not (> 0 and <= 128))
        {
            error =
                "'delegatedPrefixLength' is required, and must be between 1 and 128, for a "
                + "'PrefixDelegation' scope.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method validates a boot file name -- RFC 2131 §2's own 128-octet cap on the
    ///     fixed "file" header field <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeEntity.BootFileName" />
    ///     is ultimately written into, the one constraint <c>Dhcpv4MessageWriter</c> would
    ///     otherwise only discover -- as a real, thrown exception, mid-DORA -- the first time a
    ///     lease is actually granted from this scope.
    /// </summary>
    private static bool TryValidateBootFileName(string bootFileName, out string error)
    {
        error = null;

        if (string.IsNullOrEmpty(bootFileName))
            return true;

        if (System.Text.Encoding.ASCII.GetByteCount(bootFileName) > 128)
        {
            error = "'bootFileName' must be at most 128 bytes (RFC 2131's own 'file' field cap).";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method validates a next-server address, meaningful only alongside a real
    ///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeEntity.BootFileName" />, but
    ///     validated unconditionally here regardless of whether one is currently set -- an
    ///     operator setting this field ahead of a boot file name it will soon accompany should
    ///     never have it silently accepted as garbage.
    /// </summary>
    private static bool TryValidateNextServerAddress(string nextServerAddress, out string error)
    {
        error = null;

        if (string.IsNullOrEmpty(nextServerAddress))
            return true;

        if (!IPAddress.TryParse(nextServerAddress, out _))
        {
            error = $"'{nextServerAddress}' is not a valid IP address.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method converts a DHCP scope entity into its REST representation.
    /// </summary>
    private static DhcpScopeDetail ToDetail(DhcpScopeEntity scope) =>
        new(
            scope.Id,
            scope.Name,
            scope.ProtocolVersion,
            scope.RangeStartAddress,
            scope.RangeEndAddress,
            scope.SubnetPrefixLength,
            scope.DefaultLeaseDuration,
            scope.MaxLeaseDuration,
            scope.IsDisabled,
            scope.GatewayAddresses,
            scope.DnsServerAddresses,
            scope.Comments,
            scope.CreatedAtUtc,
            scope.UpdatedAtUtc,
            scope.DnsZoneId,
            scope.BootFileName,
            scope.NextServerAddress,
            scope.Kind,
            scope.DelegatedPrefixLength
        );
}
