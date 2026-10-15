using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This hosted service drains events from <see cref="DnsServerEventPublisher" /> one at a
///     time and delivers each to every <see cref="IDnsServerEventSubscriber{TEvent}" /> registered
///     for its exact runtime type, in registration order. It reads the control queue first and
///     the per-query queue when the control queue is empty; order inside each queue is kept.
///     Each event gets a new DI scope, so subscribers can use scoped services such as
///     repositories.
///     <para>
///         Each subscriber runs in its own <c>try</c>: an exception, thrown synchronously or from
///         the returned task, is logged naming the subscriber and the event type, and the next
///         subscriber still runs. An event another instance published (see
///         <see cref="IDnsServerRemoteEventSubscriber" />) reaches only subscribers that
///         implement that marker.
///     </para>
///     <para>
///         Delivery is sequential: a subscriber that awaits slow work inline delays every later
///         event. Subscribers that do I/O detach it and return at once.
///     </para>
/// </summary>
/// <param name="publisher">The publisher whose queued events this dispatcher drains.</param>
/// <param name="scopeFactory">The factory used to create a dependency injection scope per event.</param>
/// <param name="logger">The logger this dispatcher records delivery failures to.</param>
public sealed class DnsServerEventDispatcher(
    DnsServerEventPublisher publisher,
    IServiceScopeFactory scopeFactory,
    ILogger<DnsServerEventDispatcher> logger
) : BackgroundService
{
    /// <summary>
    ///     This field caches the reflected <see cref="IDnsServerEventSubscriber{TEvent}.HandleAsync" />
    ///     method per event type, so the lookup happens once per type rather than per event.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> HandleMethodCache = new();

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();

            if (
                !publisher.ControlReader.TryRead(out QueuedDnsServerEvent item)
                && !publisher.LossyReader.TryRead(out item)
            )
            {
                publisher.ReportDroppedEvents();
                await WaitForAnyEventAsync(stoppingToken);
                continue;
            }

            try
            {
                await DispatchAsync(item, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Resolving the subscribers failed; log it and keep draining.
                logger.LogError(
                    exception,
                    "Unhandled exception while dispatching a {EventType} event.",
                    item.Event.GetType().Name
                );
            }

            publisher.ReportDroppedEvents();
        }
    }

    /// <summary>
    ///     This method waits until either queue has an event. The loser of the two waits is
    ///     cancelled so no wait is left pending on a channel.
    /// </summary>
    private async Task WaitForAnyEventAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource waitCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        Task controlWait = publisher.ControlReader.WaitToReadAsync(waitCancellation.Token).AsTask();
        Task lossyWait = publisher.LossyReader.WaitToReadAsync(waitCancellation.Token).AsTask();

        await Task.WhenAny(controlWait, lossyWait);
        await waitCancellation.CancelAsync();

        try
        {
            await Task.WhenAll(controlWait, lossyWait);
        }
        catch (OperationCanceledException)
        {
            // The cancelled loser, or shutdown; the loop checks stoppingToken itself.
        }
    }

    /// <summary>
    ///     This method delivers one event to every subscriber registered for its exact runtime
    ///     type. Subscribers for a base type or interface are not called.
    /// </summary>
    private async Task DispatchAsync(QueuedDnsServerEvent item, CancellationToken stoppingToken)
    {
        IDnsServerEvent domainEvent = item.Event;
        Type eventType = domainEvent.GetType();
        Type subscriberInterface = typeof(IDnsServerEventSubscriber<>).MakeGenericType(eventType);

        // The subscriber interface is generic over the runtime type, so the call goes through
        // reflection; the method lookup is cached per type.
        MethodInfo handleMethod = HandleMethodCache.GetOrAdd(
            eventType,
            _ =>
                subscriberInterface.GetMethod(
                    nameof(IDnsServerEventSubscriber<IDnsServerEvent>.HandleAsync)
                )
        );

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        foreach (object subscriber in scope.ServiceProvider.GetServices(subscriberInterface))
        {
            // Each node has its own database, so a foreign event reaches only the subscribers
            // that opted in; the rest would act on ids that mean nothing here.
            if (item.IsRemote && subscriber is not IDnsServerRemoteEventSubscriber)
                continue;

            try
            {
                ValueTask handleTask = (ValueTask)
                    handleMethod.Invoke(subscriber, [domainEvent, stoppingToken]);

                await handleTask;
            }
            catch (Exception exception)
            {
                Exception reported = exception
                    is TargetInvocationException { InnerException: not null } wrapped
                    ? wrapped.InnerException
                    : exception;

                // Shutdown is not a subscriber failure: stop the loop instead of logging an error
                // for every remaining subscriber. A subscriber's own cancellation stays a failure.
                if (reported is OperationCanceledException && stoppingToken.IsCancellationRequested)
                    throw new OperationCanceledException(stoppingToken);

                // One failing subscriber must not skip the others or stop the bus.
                logger.LogError(
                    reported,
                    "Subscriber {Subscriber} failed while handling a {EventType} event.",
                    subscriber.GetType().Name,
                    eventType.Name
                );
            }
        }
    }
}
