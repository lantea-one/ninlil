using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This class is the <see cref="IDnsQueryResolver" /> for inbound RFC 1996 NOTIFY messages,
///     the receiving side of <see cref="ZoneNotifyEventSubscriber" />. It is the first resolver
///     in the chain and declines anything that is not a NOTIFY. A NOTIFY is accepted only for
///     the apex of a secondary zone and only from one of its configured primary addresses; it
///     then triggers an immediate refresh.
/// </summary>
/// <param name="zoneCache">The cache used to find the zone a NOTIFY names.</param>
/// <param name="refreshTrigger">
///     The scheduler told to check the zone's primary immediately once a NOTIFY validates.
/// </param>
public class NotifyQueryResolver(
    AuthoritativeZoneCache zoneCache,
    ISecondaryZoneRefreshTrigger refreshTrigger
) : IDnsQueryResolver
{
    /// <inheritdoc />
    public ValueTask<DnsQueryResolution> TryResolveAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    )
    {
        DnsMessage request = context.Request;

        if (request.Opcode != DnsOpcodeEnum.Notify)
            return ValueTask.FromResult(DnsQueryResolution.NotHandled);

        if (request.Questions.Count == 0)
            return ValueTask.FromResult(
                DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.FormatError))
            );

        DnsQuestion question = request.Questions[0];
        CachedZone zone = zoneCache.FindZone(question.Name);

        // NOTIFY applies only to zones this server holds as a secondary; others have no
        // primary to check the sender against and are refused. SecondaryCatalog (an ordinary
        // zone under RFC 9432) and SecondaryForwarder (its apex records are transferred like
        // any zone) use the same NOTIFY and pull machinery, so they are listed too.
        if (
            zone is null
            || zone.ApexName != question.Name
            || zone.Zone.Type
                is not (
                    ZoneTypeEnum.Secondary
                    or ZoneTypeEnum.SecondaryCatalog
                    or ZoneTypeEnum.SecondaryForwarder
                )
        )
            return ValueTask.FromResult(
                DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused))
            );

        bool senderIsKnownPrimary = zone.Zone.PrimaryServerAddresses.Any(address =>
            IPAddress.TryParse(address, out IPAddress primaryAddress)
            && primaryAddress.Equals(context.RemoteAddress)
        );

        if (!senderIsKnownPrimary)
            return ValueTask.FromResult(
                DnsQueryResolution.Handled(ErrorResponse(request, DnsResponseCodeEnum.Refused))
            );

        // RFC 1996 §3.11: acknowledging a NOTIFY only promises to check the zone, not that it
        // changed.
        refreshTrigger.RequestImmediateRefresh(zone.Zone.Id);

        DnsMessage acknowledgement = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = DnsOpcodeEnum.Notify,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions = { question },
        };

        return ValueTask.FromResult(DnsQueryResolution.Handled(acknowledgement));
    }

    /// <summary>
    ///     This method builds the reply to a rejected NOTIFY, echoing the question like the
    ///     acknowledgement does.
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
