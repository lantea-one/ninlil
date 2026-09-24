using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This class runs for the lifetime of the application, draining events from
///     <see cref="DnsServerEventPublisher" /> and delivering each one to every
///     <see cref="IDnsServerEventSubscriber{TEvent}" /> registered for that specific event's
///     type. A new dependency injection scope is created for each event so a subscriber can
///     safely depend on scoped services -- a repository backed by a database context, for
///     example -- the same way a request handler would.
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
    ///     This property caches, per concrete event type, the reflected
    ///     <see cref="IDnsServerEventSubscriber{TEvent}.HandleAsync" /> method to invoke, so that
    ///     the reflection lookup itself only ever happens once per event type rather than once
    ///     per event.
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
                // A misbehaving subscriber must never take the whole event pipeline down with
                // it; log the failure and keep draining the queue.
                logger.LogError(
                    exception,
                    "Unhandled exception while dispatching a {EventType} event.",
                    domainEvent.GetType().Name
                );
            }
        }
    }

    /// <summary>
    ///     This method delivers a single event to every subscriber registered for its exact
    ///     runtime type.
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
