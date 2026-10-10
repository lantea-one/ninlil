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
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages DHCP scopes and reports their utilization. A scope's static
///     reservations and custom options are managed through the nested
///     <see cref="DhcpReservationsController" /> (<c>/api/dhcp-scopes/{scopeId}/reservations</c>)
///     and <see cref="DhcpScopeCustomOptionsController" />
///     (<c>/api/dhcp-scopes/{scopeId}/custom-options</c>), which use the same permissions:
///     <c>dhcpscopes:read</c> for reads and <c>dhcpscopes:write</c> for changes.
/// </summary>
/// <param name="scopeRepository">Reads and writes scopes.</param>
/// <param name="reservationRepository">Removes a deleted scope's reservations (see <see cref="Delete" />).</param>
/// <param name="customOptionRepository">Removes a deleted scope's custom options (see <see cref="Delete" />).</param>
/// <param name="zoneRepository">
///     Confirms a scope's <see cref="DhcpScopeWriteRequest.DnsZoneId" /> names an existing zone.
///     Only existence is checked here; whether the zone can take DHCP registrations is decided
///     when registering, as for the CAA and PTR settings on <c>ZonesController</c>.
/// </param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="statisticsService">Computes a scope's current utilization for <see cref="Statistics" />.</param>
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
    ///     This action lists DHCP scopes, one page at a time. Requires <c>dhcpscopes:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
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
    ///     This action returns one DHCP scope. Returns 404 for an unknown id. Requires
    ///     <c>dhcpscopes:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
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
    ///     This action reports a scope's current utilization: the total addresses or prefixes in
    ///     its range, how many are leased or offered, how many its reservations account for, and
    ///     how many remain available. It is computed from the scope's settings and its recorded
    ///     leases and reservations on every call (see <see cref="IDhcpScopeStatisticsService" />).
    ///     The two slot counts are strings because an IPv6 range can exceed a 64-bit integer.
    ///     Returns 404 for an unknown id. Requires <c>dhcpscopes:read</c>.
    /// </summary>
    [HttpGet("{id:guid}/statistics")]
    [ReadFromReplica]
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
    ///     This action creates a DHCP scope. Returns 400 for a missing name, a range address of the
    ///     wrong family, a bad prefix length, a non-positive lease duration or a default above the
    ///     maximum, an invalid gateway, DNS server or next-server address, a boot file name over
    ///     128 bytes, an invalid kind and delegated-prefix combination, or an unknown DNS zone; and
    ///     409 when the name is taken. Requires <c>dhcpscopes:write</c>.
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
    ///     This action replaces a DHCP scope's fields. Validation and errors are as for
    ///     <see cref="Create" />, plus 404 for an unknown id. Requires <c>dhcpscopes:write</c>.
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
    ///     This action updates a DHCP scope as an RFC 7396 merge patch: an absent field is
    ///     unchanged, a JSON <c>null</c> clears a nullable field, and any other value replaces it.
    ///     The protocol version cannot be changed. The patched scope is checked as for
    ///     <see cref="Create" />. Requires <c>dhcpscopes:write</c>.
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
    ///     This action deletes a DHCP scope with its reservations and custom options, removed
    ///     explicitly rather than by a relational cascade delete, which MongoDB lacks. Leases
    ///     recorded for the scope (which refer to it by <c>ScopeName</c>, not id) are left alone as
    ///     operational data, as <c>Server.Backup.BackupEntityCatalog</c> treats them. A tag rule
    ///     that names the scope (<c>DhcpTagRuleEntity.ScopeId</c>) is also left alone and simply
    ///     never matches again. Returns 404 for an unknown id. Requires <c>dhcpscopes:write</c>.
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
    ///     This method returns the largest CIDR prefix length for a protocol version: 32 for IPv4,
    ///     128 for IPv6.
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
    ///     This method checks that an address parses and belongs to the family of
    ///     <paramref name="protocolVersion" />.
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
    ///     This method parses a merge-patch field holding a JSON array of IP address strings; an
    ///     empty array means no addresses.
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
    ///     This method checks every field of a scope write request before anything is saved, so a
    ///     POST or PUT either succeeds fully or changes nothing.
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
    ///     This method checks the scope kind against <c>DelegatedPrefixLength</c>. A
    ///     <see cref="DhcpScopeKindEnum.PrefixDelegation" /> scope must be IPv6 (DHCPv4 has no
    ///     prefix delegation; see <c>DhcpScopeKindEnum</c>) and needs a delegated prefix length
    ///     from 1 to 128. An <see cref="DhcpScopeKindEnum.AddressAssignment" /> scope (every DHCPv4
    ///     scope and an ordinary DHCPv6 one) has no delegated prefix length, so setting one is
    ///     rejected rather than ignored.
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

        // Kind is PrefixDelegation from here on.
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
    ///     This method checks the boot file name against the 128-octet "file" header field of RFC
    ///     2131 §2 that
    ///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeEntity.BootFileName" /> is
    ///     written into. Otherwise <c>Dhcpv4MessageWriter</c> would throw in the middle of the
    ///     first exchange that sends it.
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
    ///     This method checks a next-server address. It only matters with a
    ///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeEntity.BootFileName" />, but is
    ///     checked even without one, so a value set ahead of the boot file name is not saved as
    ///     garbage.
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
    ///     This method converts a scope entity to its API representation.
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
