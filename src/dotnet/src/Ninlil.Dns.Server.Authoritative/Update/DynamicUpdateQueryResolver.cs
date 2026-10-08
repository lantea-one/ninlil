using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Update;

/// <summary>
///     This class is the <see cref="IDnsQueryResolver" /> for RFC 2136 dynamic update (the
///     UPDATE opcode). It answers FORMERR unless the Zone Section is one SOA/IN question, NOTAUTH
///     when this server does not host that zone or the name is not its apex, and REFUSED unless
///     the zone is a Primary with <see cref="ZoneEntity.DynamicUpdateEnabled" /> and the request
///     carries a TSIG signature that the pipeline verified
///     (<see cref="DnsQueryContext.TransactionSignature" />) with a key listed in
///     <see cref="ZoneEntity.UpdateTsigKeyNames" />. It then checks the prerequisites
///     and prescans the update section with <see cref="DynamicUpdateProcessor" /> and applies
///     the result through <see cref="IZoneUpdateService" />.
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

        // RFC 2136 §3.1.1: the Zone Section holds exactly one entry.
        if (request.Questions.Count != 1)
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.FormatError)
            );

        // RFC 2136 §3.1.1: the Zone Section names the zone by SOA/IN, not an arbitrary query type.
        DnsQuestion zoneSection = request.Questions[0];

        if (
            zoneSection.Type != DnsResourceRecordTypeEnum.Soa
            || zoneSection.Class != DnsClassEnum.Internet
        )
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.FormatError)
            );

        CachedZone zone = zoneCache.FindZone(zoneSection.Name);

        // RFC 2136 §3.1.2: a server that does not host the named zone, or is given a name that is
        // not the zone's apex, answers NOTAUTH.
        if (zone is null || zone.ApexName != zoneSection.Name)
            return DnsQueryResolution.Handled(
                ErrorResponse(request, DnsResponseCodeEnum.NotAuthoritative)
            );

        // Only a Primary zone accepts updates; forwarding updates from a secondary to its
        // primary is not supported.
        if (zone.Zone.Type != ZoneTypeEnum.Primary || !zone.Zone.DynamicUpdateEnabled)
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
