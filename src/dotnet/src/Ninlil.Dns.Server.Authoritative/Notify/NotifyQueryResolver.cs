using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This class implements <see cref="IDnsQueryResolver" /> for inbound RFC 1996 NOTIFY
///     messages -- the receiving half of the outbound NOTIFY sending
///     <see cref="ZoneNotifyEventSubscriber" /> already does. Unlike an ordinary query, a NOTIFY
///     always fits the single request/response shape <see cref="IDnsQueryResolver" /> expects,
///     so this needs none of <see cref="Transfer.IZoneTransferService" />'s separate,
///     multi-message contract.
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

        // RFC 1996 only makes sense for a name this server hosts as somebody else's secondary --
        // a NOTIFY about a name it doesn't host at all, or about its own primary zone (which has
        // no "primary server" of its own to validate the sender against), is refused.
        // SecondaryCatalog and SecondaryForwarder are included alongside Secondary: an RFC 9432
        // catalog zone is, per its own spec text, "a regular DNS zone", and a SecondaryForwarder
        // zone's own apex SOA/TXT content (see Forwarding.ForwarderZoneContentSyncService) is,
        // from this resolver's own point of view, an ordinary AXFR/IXFR-transferable zone like
        // any other -- both consume exactly the same RFC 1996 NOTIFY / AXFR-IXFR-pull machinery
        // any other secondary does, so it must be listed here explicitly or NOTIFY silently ignores it.
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

        // Per RFC 1996 §3.11, acknowledging a NOTIFY is a promise to "go to work" and check
        // whether the zone actually changed -- it says nothing about whether it did.
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
    ///     This method builds the single message a rejected NOTIFY is answered with, echoing the
    ///     question section back the same way <see cref="TryResolveAsync" />'s own acknowledgement
    ///     already does.
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
