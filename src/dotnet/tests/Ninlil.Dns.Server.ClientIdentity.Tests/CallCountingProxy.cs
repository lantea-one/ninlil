using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class wraps a real service in a proxy that counts calls by method name, so a test can
///     show that the code under test reads the repository once per refresh and not once per
///     query. <see cref="Decorate" /> swaps it into a service collection in place of
///     the registered implementation, which keeps working against the real database.
/// </summary>
/// <typeparam name="T">The interface to wrap.</typeparam>
public class CallCountingProxy<T> : DispatchProxy
{
    private T _target;

    private ConcurrentDictionary<string, int> _counts;

    /// <summary>
    ///     This method wraps <paramref name="target" /> and records its calls in
    ///     <paramref name="counts" />.
    /// </summary>
    public static T Create(T target, ConcurrentDictionary<string, int> counts)
    {
        T proxy = Create<T, CallCountingProxy<T>>();
        CallCountingProxy<T> typed = (CallCountingProxy<T>)(object)proxy;
        typed._target = target;
        typed._counts = counts;
        return proxy;
    }

    /// <summary>
    ///     This method replaces the registration of <typeparamref name="T" /> with a scoped
    ///     counting wrapper around the originally registered implementation.
    /// </summary>
    public static void Decorate(
        IServiceCollection services,
        ConcurrentDictionary<string, int> counts
    )
    {
        ServiceDescriptor original = services.Last(descriptor =>
            descriptor.ServiceType == typeof(T)
        );
        Type implementation = original.ImplementationType;
        services.Remove(original);
        services.AddScoped(implementation);
        services.AddScoped(
            typeof(T),
            provider => Create((T)provider.GetRequiredService(implementation), counts)
        );
    }

    /// <inheritdoc />
    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        _counts.AddOrUpdate(targetMethod.Name, 1, (_, count) => count + 1);

        try
        {
            return targetMethod.Invoke(_target, args);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }
}
