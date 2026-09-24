using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This class sends an RFC 1996 NOTIFY to every one of a zone's configured secondaries
///     whenever <see cref="DnsZoneChangedEvent" /> reports that it changed.
///     <para>
///         Sending, and every retry, happens in a detached background task rather than being
///         awaited directly from <see cref="HandleAsync" />: <see cref="DnsServerEventDispatcher" />
///         processes events one at a time, and a secondary that is slow or unreachable could
///         otherwise stall every other subscriber's handling of every other event -- including,
///         for a completely unrelated zone, <see cref="ZoneChangedEventSubscriber" />'s own cache
///         reload -- for as long as this server keeps retrying. This is also why the background
///         task opens its own dependency injection scope rather than using the constructor's:
///         the dispatcher disposes the scope <see cref="HandleAsync" /> was invoked in as soon as
///         every subscriber's own <see cref="System.Threading.Tasks.ValueTask" /> completes, which
///         happens well before the detached work here is done.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh set of scoped services.</param>
/// <param name="logger">The logger this subscriber records delivery failures to.</param>
public class ZoneNotifyEventSubscriber(
    IServiceScopeFactory scopeFactory,
    ILogger<ZoneNotifyEventSubscriber> logger
) : IDnsServerEventSubscriber<DnsZoneChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DnsZoneChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = NotifySecondariesAsync(domainEvent, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method does the actual work: loading the zone's current SOA and configured
    ///     secondaries, then notifying each of them.
    /// </summary>
    private async Task NotifySecondariesAsync(
        DnsZoneChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IZoneRepository zoneRepository =
                scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            IRecordRepository recordRepository =
                scope.ServiceProvider.GetRequiredService<IRecordRepository>();
            INotifySender notifySender = scope.ServiceProvider.GetRequiredService<INotifySender>();

            ZoneEntity zone = await zoneRepository.GetByIdAsync(domainEvent.ZoneId, stoppingToken);

            if (zone is null || zone.NotifyAddresses.Count == 0)
                return;

            IReadOnlyList<RecordEntity> soaRecords = await recordRepository.FindByNameAsync(
                domainEvent.ZoneId,
                zone.Name,
                DnsResourceRecordTypeEnum.Soa,
                stoppingToken
            );

            if (soaRecords.Count == 0)
                return;

            DnsName zoneName = DnsName.Parse(zone.Name);
            DnsResourceRecord soaWireRecord = RecordEntityCodec.ToWireRecord(
                soaRecords[0],
                zoneName
            );

            foreach (string addressText in zone.NotifyAddresses)
            {
                stoppingToken.ThrowIfCancellationRequested();

                if (!IPAddress.TryParse(addressText, out IPAddress address))
                {
                    logger.LogWarning(
                        "Zone '{ZoneName}' has an invalid NOTIFY address '{Address}'; skipping it.",
                        zone.Name,
                        addressText
                    );
                    continue;
                }

                // Per RFC 1996 §3.7, a NOTIFY carries the AA bit -- an assertion by the primary
                // that it is genuinely authoritative for the zone -- and, as a hint, the current
                // SOA the secondary can compare against before deciding whether to bother
                // pulling a transfer at all.
                DnsMessage notifyMessage = new()
                {
                    Id = (ushort)Random.Shared.Next(ushort.MaxValue + 1),
                    Opcode = DnsOpcodeEnum.Notify,
                    IsAuthoritativeAnswer = true,
                    Questions =
                    {
                        new DnsQuestion(
                            zoneName,
                            DnsResourceRecordTypeEnum.Soa,
                            DnsClassEnum.Internet
                        ),
                    },
                    Answers = { soaWireRecord },
                };

                bool acknowledged = await notifySender.SendAsync(
                    address,
                    notifyMessage,
                    stoppingToken
                );

                if (!acknowledged)
                    logger.LogWarning(
                        "No NOTIFY acknowledgement from {Address} for zone '{ZoneName}' after every attempt.",
                        address,
                        zone.Name
                    );
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while sending NOTIFY for zone '{ZoneName}'.",
                domainEvent.ZoneName
            );
        }
    }
}
