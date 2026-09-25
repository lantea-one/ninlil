using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Update;

/// <summary>
///     This class is the <see cref="IDnsQueryResolver" /> for RFC 2136 dynamic update (the
///     UPDATE opcode). It requires the zone to be a Primary with
///     <see cref="ZoneEntity.DynamicUpdateEnabled" /> and the request to carry a valid TSIG
///     signature, checks the prerequisites and update section with
///     <see cref="DynamicUpdateProcessor" />, and applies the result through
///     <see cref="IZoneUpdateService" />.
///     <para>
///         The enable flag and apex come from <see cref="AuthoritativeZoneCache" />, which may be
///         briefly stale; that only moves the moment an update starts or stops being accepted.
///         The records themselves are read from the repositories in a new scope, because
///         prerequisites must be checked against current data and the cache is updated later
///         through the event bus.
///     </para>
/// </summary>
/// <param name="zoneCache">The cache used to find the zone an update names.</param>
/// <param name="scopeFactory">
///     The factory used to create a scope for the repositories, the TSIG service and
///     <see cref="IZoneUpdateService" />.
/// </param>
public class DynamicUpdateQueryResolver(
    AuthoritativeZoneCache zoneCache,
    IServiceScopeFactory scopeFactory
) : IDnsQueryResolver
{
    /// <inheritdoc />
    public async ValueTask<DnsQueryResolution> TryResolveAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    )
    {
        DnsMessage request = context.Request;

        if (request.Opcode != DnsOpcodeEnum.Update)
            return DnsQueryResolution.NotHandled;

        if (request.Questions.Count == 0)
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.FormatError)
            );

        // RFC 2136 §3.1: the Zone Section names the zone by SOA/IN, not an arbitrary query type.
        DnsQuestion zoneSection = request.Questions[0];

        if (
            zoneSection.Type != DnsResourceRecordTypeEnum.Soa
            || zoneSection.Class != DnsClassEnum.Internet
        )
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.FormatError)
            );

        CachedZone zone = zoneCache.FindZone(zoneSection.Name);

        // Only a Primary zone accepts updates; forwarding updates from a secondary to its
        // primary is not supported.
        if (
            zone is null
            || zone.ApexName != zoneSection.Name
            || zone.Zone.Type != ZoneTypeEnum.Primary
            || !zone.Zone.DynamicUpdateEnabled
        )
            return DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused));

        // Dynamic update always requires TSIG; there is no address-based allowance, as for zone
        // transfers.
        if (request.Tsig is null)
            return DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused));

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        TsigVerificationResult tsigResult = await tsigService.VerifyAsync(
            context.RawMessage,
            context.TsigRecordStartOffset,
            request,
            stoppingToken
        );

        if (!tsigResult.IsValid)
            return DnsQueryResolution.Handled(ErrorResponse(request, tsigResult.Error));

        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> currentRecords = await recordRepository.ListByZoneIdAsync(
            zone.Zone.Id,
            stoppingToken
        );

        // The Prerequisite section is the Answer section; RFC 2136 §3.2 checks it against the
        // zone before any update is applied.
        DnsResponseCodeEnum prerequisiteResult = DynamicUpdateProcessor.CheckPrerequisites(
            request.Answers,
            currentRecords
        );

        if (prerequisiteResult != DnsResponseCodeEnum.NoError)
            return DnsQueryResolution.Handled(ErrorResponse(request, prerequisiteResult));

        // The Update section is the request's Authority section.
        if (
            !DynamicUpdateProcessor.IsUpdateSectionValid(
                request.AuthorityRecords,
                zoneSection.Class
            )
        )
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.FormatError)
            );

        (List<RecordEntity> toAdd, List<RecordEntity> toRemove) =
            DynamicUpdateProcessor.ComputeDiff(
                request.AuthorityRecords,
                currentRecords,
                zone.Zone.Id,
                zone.ApexName
            );

        if (toAdd.Count > 0 || toRemove.Count > 0)
        {
            IZoneUpdateService zoneUpdateService =
                scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();
            await zoneUpdateService.ApplyChangesAsync(
                zone.Zone.Id,
                toAdd,
                toRemove,
                null,
                stoppingToken
            );
        }

        return DnsQueryResolution.Handled(SuccessResponse(request, zoneSection));
    }

    /// <summary>
    ///     This method builds the reply to an applied (or empty but valid) update.
    /// </summary>
    private static DnsMessage SuccessResponse(DnsMessage request, DnsQuestion zoneSection) =>
        new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions = { zoneSection },
        };

    /// <summary>
    ///     This method builds the reply to a rejected update, echoing the question. Error replies
    ///     are never signed, which also covers the RFC 8945 §5.2 rule against signing with an
    ///     unknown key. Successful replies are not TSIG-signed either.
    /// </summary>
    private static DnsMessage ErrorResponse(DnsMessage request, DnsResponseCodeEnum responseCode) =>
        new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            ResponseCode = responseCode,
            Questions = [.. request.Questions],
        };
}
