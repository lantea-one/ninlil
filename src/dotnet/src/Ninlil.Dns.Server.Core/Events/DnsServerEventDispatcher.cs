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
    ///     This field caches, per event type, the closed subscriber interface and the typed call
    ///     into it, so reflection runs once per type and never per event.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, EventInvoker> InvokerCache = new();

    /// <summary>
    ///     This delegate type calls <see cref="IDnsServerEventSubscriber{TEvent}.HandleAsync" /> on
    ///     a subscriber whose event type is only known at run time.
    /// </summary>
    private delegate ValueTask HandleInvoker(
        object subscriber,
        IDnsServerEvent domainEvent,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This record holds what dispatching one event type needs: the closed
    ///     <see cref="IDnsServerEventSubscriber{TEvent}" /> interface to resolve from DI, and the
    ///     delegate that calls its <c>HandleAsync</c>.
    /// </summary>
    private sealed record EventInvoker(Type SubscriberInterface, HandleInvoker Invoke);

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
    ///     This method is the body of every <see cref="HandleInvoker" />, closed over the event
    ///     type once per type. The casts are exact because the subscriber was resolved as
    ///     <see cref="IDnsServerEventSubscriber{TEvent}" /> for the event's runtime type.
    /// </summary>
    private static ValueTask InvokeTyped<TEvent>(
        object subscriber,
        IDnsServerEvent domainEvent,
        CancellationToken stoppingToken
    )
        where TEvent : IDnsServerEvent =>
        ((IDnsServerEventSubscriber<TEvent>)subscriber).HandleAsync(
            (TEvent)domainEvent,
            stoppingToken
        );

    /// <summary>
    ///     This method builds the cached invoker for one event type. The generic method is
    ///     closed once with <c>MakeGenericMethod</c> and turned into a delegate, so a delivery
    ///     is an ordinary delegate call with no <c>object[]</c> and no boxed token.
    /// </summary>
    private static EventInvoker CreateInvoker(Type eventType)
    {
        Type subscriberInterface = typeof(IDnsServerEventSubscriber<>).MakeGenericType(eventType);
        MethodInfo typed = typeof(DnsServerEventDispatcher)
            .GetMethod(nameof(InvokeTyped), BindingFlags.NonPublic | BindingFlags.Static)
            .MakeGenericMethod(eventType);

        return new EventInvoker(
            subscriberInterface,
            (HandleInvoker)typed.CreateDelegate(typeof(HandleInvoker))
        );
    }

    /// <summary>
    ///     This method delivers one event to every subscriber registered for its exact runtime
    ///     type. Subscribers for a base type or interface are not called.
    /// </summary>
    private async Task DispatchAsync(QueuedDnsServerEvent item, CancellationToken stoppingToken)
    {
        IDnsServerEvent domainEvent = item.Event;
        Type eventType = domainEvent.GetType();
        EventInvoker invoker = InvokerCache.GetOrAdd(eventType, CreateInvoker);

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        foreach (
            object subscriber in scope.ServiceProvider.GetServices(invoker.SubscriberInterface)
        )
        {
            // Each node has its own database, so a foreign event reaches only the subscribers
            // that opted in; the rest would act on ids that mean nothing here.
            if (item.IsRemote && subscriber is not IDnsServerRemoteEventSubscriber)
                continue;

            try
            {
                await invoker.Invoke(subscriber, domainEvent, stoppingToken);
            }
            catch (Exception exception)
            {
                // Shutdown is not a subscriber failure: stop the loop instead of logging an error
                // for every remaining subscriber. A subscriber's own cancellation stays a failure.
                if (
                    exception is OperationCanceledException
                    && stoppingToken.IsCancellationRequested
                )
                    throw new OperationCanceledException(stoppingToken);

                // One failing subscriber must not skip the others or stop the bus.
                logger.LogError(
                    exception,
                    "Subscriber {Subscriber} failed while handling a {EventType} event.",
                    subscriber.GetType().Name,
                    eventType.Name
                );
            }
        }
    }
}
