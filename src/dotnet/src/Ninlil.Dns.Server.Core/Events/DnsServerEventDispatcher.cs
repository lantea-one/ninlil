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
///     for its exact runtime type, in registration order. Each event gets a new DI scope, so
///     subscribers can use scoped services such as repositories. Delivery is sequential: a slow
///     subscriber delays every later event, and if one subscriber throws, the remaining
///     subscribers for that event are skipped (the error is logged and the next event
///     proceeds).
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
        await foreach (IDnsServerEvent domainEvent in publisher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(domainEvent, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A failing subscriber must not stop the bus; log it and keep draining.
                logger.LogError(
                    exception,
                    "Unhandled exception while dispatching a {EventType} event.",
                    domainEvent.GetType().Name
                );
            }
        }
    }

    /// <summary>
    ///     This method delivers one event to every subscriber registered for its exact runtime
    ///     type. Subscribers for a base type or interface are not called.
    /// </summary>
    private async Task DispatchAsync(IDnsServerEvent domainEvent, CancellationToken stoppingToken)
    {
        Type eventType = domainEvent.GetType();
        Type subscriberInterface = typeof(IDnsServerEventSubscriber<>).MakeGenericType(eventType);

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
            ValueTask handleTask = (ValueTask)
                handleMethod.Invoke(subscriber, [domainEvent, stoppingToken]);

            await handleTask;
        }
    }
}
