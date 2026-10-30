using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Update;

/// <summary>
///     This class is the <see cref="IDnsQueryResolver" /> for RFC 2136 dynamic update (the
///     UPDATE opcode). It answers FORMERR unless the Zone Section is one SOA/IN question, NOTAUTH
///     when this server does not host that zone or the name is not its apex, and REFUSED unless
///     the zone is a Primary or CloudPrimary with <see cref="ZoneEntity.DynamicUpdateEnabled" /> and the request
///     carries a TSIG signature that the pipeline verified
///     (<see cref="DnsQueryContext.TransactionSignature" />) with a key listed in
///     <see cref="ZoneEntity.UpdateTsigKeyNames" />. It then checks the prerequisites
///     and prescans the update section with <see cref="DynamicUpdateProcessor" /> and applies
///     the result through <see cref="IZoneUpdateService" />.
///     <para>
///         A CloudPrimary zone keeps its local records as the source of truth. An accepted update
///         is applied locally exactly as for a Primary zone and answered NOERROR at once; the
///         resolver then asks <see cref="ICloudZoneSyncTrigger.RequestSync" /> to push the zone, so
///         the answer never waits for the provider (a provider call can take seconds and the
///         client would retransmit). A failed push is reported by the sync service (a warning and
///         <c>CloudDnsZoneSyncFailedEvent</c>) and the next push sends the complete local state.
///         An update that adds a record the providers cannot hold is refused as a whole
///         (<see cref="CloudZoneUpdatePolicy.IsPushable" />). The provider's own SOA serial is
///         unrelated to the local one, because the SOA is never pushed. Without an
///         <see cref="ICloudZoneSyncTrigger" /> the zone waits for its sync interval.
///     </para>
///     <para>
///         An update to a zone without an SOA gets SERVFAIL and one warning, because the serial
///         cannot be advanced.
///     </para>
///     <para>
///         The enable flag and apex come from <see cref="AuthoritativeZoneCache" />, which may be
///         briefly stale; that only moves the moment an update starts or stops being accepted.
///         The records themselves are read from the repositories in a new scope, because
///         prerequisites must be checked against current data and the cache is updated later
///         through the event bus.
///     </para>
///     <para>
///         Replies are plain messages. The pipeline signs them as its last step
///         (<see cref="IDnsTransactionSignatureService" />), and a request whose signature fails
///         never reaches this class: the pipeline answers it with the RFC 8945 §5.3.2 error reply.
///     </para>
/// </summary>
/// <param name="zoneCache">The cache used to find the zone an update names.</param>
/// <param name="scopeFactory">
///     The factory used to create a scope for the repositories and
///     <see cref="IZoneUpdateService" />.
/// </param>
/// <param name="cloudSync">
///     The trigger used to request a push after a CloudPrimary update, or <see langword="null" />
///     when the zone should wait for its regular sync.
/// </param>
/// <param name="logger">The logger for the no-SOA warning and the applied-update message.</param>
public class DynamicUpdateQueryResolver(
    AuthoritativeZoneCache zoneCache,
    IServiceScopeFactory scopeFactory,
    ICloudZoneSyncTrigger cloudSync = null,
    ILogger<DynamicUpdateQueryResolver> logger = null
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

        // RFC 2136 §3.1.1: the Zone Section holds exactly one entry.
        if (request.Questions.Count != 1)
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.FormatError)
            );

        // RFC 2136 §3.1.1: the Zone Section's type must be SOA, else FORMERR. That check comes
        // first, as in the RFC.
        DnsQuestion zoneSection = request.Questions[0];

        if (zoneSection.Type != DnsResourceRecordTypeEnum.Soa)
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.FormatError)
            );

        // RFC 2136 §3.1.1: ZCLASS is the class of the zone, and "the ZNAME and ZCLASS are checked
        // to see if the zone so named is one of this server's authority zones, else signal
        // NOTAUTH". This server hosts class IN only, so any other class names no zone here.
        if (zoneSection.Class != DnsClassEnum.Internet)
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.NotAuthoritative)
            );

        CachedZone zone = zoneCache.FindZone(zoneSection.Name);

        // RFC 2136 §3.1.1: a server that does not host the named zone, or is given a name that is
        // not the zone's apex, answers NOTAUTH.
        if (zone is null || zone.ApexName != zoneSection.Name)
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.NotAuthoritative)
            );

        // Primary and CloudPrimary are the zone types this server originates. The other types are
        // refused because their content comes from elsewhere, and forwarding an update to the
        // primary (RFC 2136 §6) is not supported. A CloudPrimary zone needs the same opt-in and
        // key binding as a Primary zone.
        bool isCloudPrimary = zone.Zone.Type == ZoneTypeEnum.CloudPrimary;

        if (
            (zone.Zone.Type != ZoneTypeEnum.Primary && !isCloudPrimary)
            || !zone.Zone.DynamicUpdateEnabled
        )
            return DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused));

        // Dynamic update always requires TSIG; there is no address-based allowance, as for zone
        // transfers. The pipeline verified the signature; without that outcome (no TSIG service
        // registered) the update is refused rather than applied unauthenticated. RFC 2136 §3.3
        // checks permission after the prerequisites; this check comes first on purpose, so a
        // sender without a valid key learns nothing about the zone from prerequisite results.
        if (request.Tsig is null || context.TransactionSignature is not { IsVerified: true })
            return DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused));

        // The key must also be bound to this zone. The pipeline signs this REFUSED, because the
        // request's signature verified.
        if (
            !ZoneTsigKeyBinding.IsAllowed(
                zone.Zone.UpdateTsigKeyNames,
                context.TransactionSignature.KeyName
            )
        )
            return DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused));

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> currentRecords = await recordRepository.ListByZoneIdAsync(
            zone.Zone.Id,
            stoppingToken
        );

        // Without an SOA the serial cannot be advanced and ApplyChangesAsync would throw. SERVFAIL
        // (RFC 1035 §4.1.1: the server could not process the request) is the honest answer, and
        // it comes after the TSIG checks, so an unauthenticated sender learns nothing.
        if (!currentRecords.Any(record => record.Type == DnsResourceRecordTypeEnum.Soa))
        {
            logger?.LogWarning(
                "Zone '{ZoneName}' has no SOA record; add one before it can accept dynamic updates.",
                zone.Zone.Name
            );
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.ServerFailure)
            );
        }

        // The Prerequisite section is the Answer section; RFC 2136 §3.2 checks it against the
        // zone before any update is applied.
        DnsResponseCodeEnum prerequisiteResult = DynamicUpdateProcessor.CheckPrerequisites(
            request.Answers,
            currentRecords,
            zone.ApexName
        );

        if (prerequisiteResult != DnsResponseCodeEnum.NoError)
            return DnsQueryResolution.Handled(ErrorResponse(request, prerequisiteResult));

        // The Update section is the request's Authority section (RFC 2136 §3.4.1 prescan).
        DnsResponseCodeEnum prescanResult = DynamicUpdateProcessor.PrescanUpdateSection(
            request.AuthorityRecords,
            zoneSection.Class,
            zone.ApexName
        );

        if (prescanResult != DnsResponseCodeEnum.NoError)
            return DnsQueryResolution.Handled(ErrorResponse(request, prescanResult));

        (List<RecordEntity> toAdd, List<RecordEntity> toRemove) =
            DynamicUpdateProcessor.ComputeDiff(
                request.AuthorityRecords,
                currentRecords,
                zone.Zone.Id,
                zone.ApexName
            );

        // A CloudPrimary zone is served by the provider's servers too, so an added record the
        // providers cannot hold would be answered locally and never publicly, while the client
        // sees success. Refuse the whole update (RFC 2136 §3.4.2.1: a failed update is undone as
        // a whole, so nothing is applied). Deletions are always allowed.
        if (
            isCloudPrimary
            && toAdd.Find(record => !CloudZoneUpdatePolicy.IsPushable(record, zone.Zone.Name))
                is { } unpushable
        )
        {
            logger?.LogDebug(
                "Refused an update to cloud zone '{ZoneName}': a {RecordType} record cannot be pushed to the provider.",
                zone.Zone.Name,
                unpushable.Type
            );
            return DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused));
        }

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

            // The change is committed locally, which is the point RFC 2136 §3.5 requires before
            // the reply. The push is only requested: it does not delay the answer, and a failed
            // push heals on the next pass.
            if (isCloudPrimary)
            {
                logger?.LogInformation(
                    "Applied a dynamic update to cloud zone '{ZoneName}' ({Added} added, {Removed} removed); a push to the provider is requested.",
                    zone.Zone.Name,
                    toAdd.Count,
                    toRemove.Count
                );
                cloudSync?.RequestSync(zone.Zone.Id);
            }
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
    ///     This method builds the reply to a rejected update, echoing the question. The response
    ///     code is a plain RCODE; TSIG errors never reach this method, because the pipeline
    ///     answers them. The pipeline signs the reply.
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
