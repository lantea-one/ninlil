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
///     This class implements <see cref="IDnsQueryResolver" /> for RFC 2136 dynamic update. Like
///     <see cref="Notify.NotifyQueryResolver" />, an update's response is always a single
///     message, so this fits the ordinary resolver shape without any of
///     <see cref="Transfer.IZoneTransferService" />'s multi-message machinery.
///     <para>
///         This resolver reads the zone's own <see cref="ZoneEntity.DynamicUpdateEnabled" /> flag
///         and apex name from the (possibly briefly stale) in-memory <see cref="AuthoritativeZoneCache" />,
///         since staleness there only risks a rejected update arriving a moment too early or
///         late. Applying the update itself, though, always reads the zone's records fresh from
///         a new dependency injection scope's own repositories rather than the cache: dynamic
///         update needs read-your-own-writes consistency against genuinely current data, which a
///         cache updated asynchronously through the event bus cannot guarantee under concurrent
///         or rapid updates.
///     </para>
/// </summary>
/// <param name="zoneCache">The cache used to find the zone an update names.</param>
/// <param name="scopeFactory">
///     The factory used to reach a fresh set of scoped services -- the repositories, the TSIG
///     service, and <see cref="IZoneUpdateService" /> -- for the actual read-and-apply work.
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

        // Dynamic update only ever applies to a zone this server is itself primary for -- a
        // secondary has no local authority to accept a write against, and would need to forward
        // it upstream instead, which is out of scope here.
        if (
            zone is null
            || zone.ApexName != zoneSection.Name
            || zone.Zone.Type != ZoneTypeEnum.Primary
            || !zone.Zone.DynamicUpdateEnabled
        )
            return DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused));

        // A dynamic update always requires TSIG, per this project's design decision -- there is
        // no IP-allowlist-only fallback, the same as zone transfers.
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

        // The Prerequisite section is the request's Answer section; RFC 2136 §3.2 requires
        // every prerequisite to hold against the zone's state *before* any update is applied.
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
    ///     This method builds the message a successfully-applied (or trivially empty, but
    ///     otherwise valid) update is answered with.
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
    ///     This method builds the single message a rejected update is answered with. Left
    ///     unsigned, on the same reasoning as every other rejection response in this project's
    ///     TSIG-authenticated operations: never signing an error response can never fall on the
    ///     wrong side of RFC 8945 §5.2.2's rule against signing back when the key itself is
    ///     unrecognized. The question section is always echoed back too, matching
    ///     <see cref="SuccessResponse" />'s own already-correct behavior.
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
