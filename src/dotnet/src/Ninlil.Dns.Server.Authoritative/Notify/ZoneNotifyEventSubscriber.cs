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
///     This class sends an RFC 1996 NOTIFY to each of a zone's configured secondaries when a
///     <see cref="DnsZoneChangedEvent" /> arrives.
///     <para>
///         The sending and retries run in a detached task instead of being awaited in
///         <see cref="HandleAsync" />. <see cref="DnsServerEventDispatcher" /> handles events one
///         at a time, so a slow or unreachable secondary would otherwise hold up every other
///         event, including cache reloads for other zones. The task creates its DI scope
///         because the dispatcher disposes the handler's scope as soon as it returns.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create the detached task's scope.</param>
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
    ///     This method loads the zone's SOA and secondaries and notifies each of them.
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

                // RFC 1996 §3.7: a NOTIFY sets AA and may carry the current SOA as a hint, so
                // the secondary can skip a transfer if it is already current.
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
