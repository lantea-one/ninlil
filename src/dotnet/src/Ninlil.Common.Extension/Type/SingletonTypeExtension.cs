using System.Collections.Concurrent;

// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This static class keeps one process-wide instance per concrete class type, created on
///     demand from a <see cref="System.Type" />. <c>Create*</c> always replaces the stored
///     instance; <c>Get*</c> returns it, creating it first if needed. The store is safe for
///     concurrent use: concurrent first <c>Get*</c> calls for one type construct exactly one
///     instance and all receive it. A constructor that throws leaves no entry, so the next call
///     tries again.
/// </summary>
// ReSharper disable once UnusedType.Global
public static class SingletonTypeExtension
{
    /// <summary>
    ///     This field holds the stored instances, at most one per type. Each value is a
    ///     <see cref="Lazy{T}" /> in <see cref="LazyThreadSafetyMode.ExecutionAndPublication" /> mode:
    ///     <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})" /> may run
    ///     its factory on several threads and keep only one result, so the factory builds the cheap
    ///     <see cref="Lazy{T}" /> and the constructor itself runs once, on the thread that reads
    ///     <see cref="Lazy{T}.Value" /> first.
    /// </summary>
    private static readonly ConcurrentDictionary<System.Type, Lazy<object>> SingletonInstances =
        new();

    /// <summary>
    ///     This method returns the stored instance of the type, constructing it with
    ///     <paramref name="factory" /> when there is none.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to retrieve the Singleton for.</param>
    /// <param name="factory">Builds the instance; runs at most once for a stored entry.</param>
    /// <returns>The stored instance.</returns>
    private static object GetOrCreate(System.Type typeInstance, Func<object> factory)
    {
        // Get or add atomically so concurrent first calls share one Lazy.
        Lazy<object> lazy = SingletonInstances.GetOrAdd(
            typeInstance,
            _ => new Lazy<object>(factory, LazyThreadSafetyMode.ExecutionAndPublication)
        );

        try
        {
            return lazy.Value;
        }
        catch
        {
            // A Lazy caches the exception it threw. Remove this exact entry (not a newer one that
            // another thread stored) so the next call constructs again.
            SingletonInstances.TryRemove(
                new KeyValuePair<System.Type, Lazy<object>>(typeInstance, lazy)
            );
            throw;
        }
    }

    /// <summary>
    ///     This method constructs a new instance with <paramref name="factory" /> and stores it,
    ///     replacing any instance already stored for the type.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to create the Singleton for.</param>
    /// <param name="factory">Builds the instance.</param>
    /// <returns>The new instance.</returns>
    private static object Replace(System.Type typeInstance, Func<object> factory)
    {
        // Build first, so a throwing constructor leaves the old entry in place.
        object instance = factory();

        // Publish the finished instance in one step.
        SingletonInstances[typeInstance] = new Lazy<object>(instance);

        return instance;
    }

    /// <summary>
    ///     This method ensures that the <see cref="System.Type" /> <paramref name="typeInstance" /> supports being a singleton.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to check.</param>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    private static void EnsureCanSingleton(System.Type typeInstance)
    {
        // Ensure we can create an instance of the type.
        if (!typeInstance.IsClass)
            throw new ArgumentException("Type must be a class.");

        // Ensure the type is NOT abstract.
        if (typeInstance.IsAbstract)
            throw new ArgumentException("Type cannot be abstract.");
    }

    /// <summary>
    ///     This method determines whether or not the <see cref="System.Type" /> <paramref name="typeInstance" /> supports being a singleton.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A boolean denoting whether or not the <see cref="System.Type" /> <paramref name="typeInstance" /> supports being a singleton.</returns>
    // ReSharper disable once UnusedMember.Global
    public static bool CanSingleton(this System.Type typeInstance) =>
        !typeInstance.IsAbstract && typeInstance.IsClass;

    /// <summary>
    ///     This method creates a new instance of the type and stores it, replacing any instance
    ///     already stored for that type.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to create the Singleton for.</param>
    /// <returns>The singleton instance of <see cref="System.Type" />.</returns>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    // ReSharper disable once MemberCanBePrivate.Global
    public static dynamic CreateSingletonInstance(this System.Type typeInstance)
    {
        // Ensure we can create a singleton instance of the type.
        EnsureCanSingleton(typeInstance);

        // Create a new instance of the type and store it in place of any existing one.
        return Replace(typeInstance, () => typeInstance.CreateInstance());
    }

    /// <summary>
    ///     This method creates a singleton instance of a type.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to create the Singleton for.</param>
    /// <param name="constructorArguments">The arguments to pass to the <paramref name="typeInstance" /> constructor.</param>
    /// <returns>The singleton instance of <see cref="System.Type" />.</returns>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    // ReSharper disable once MemberCanBePrivate.Global
    public static dynamic CreateSingletonInstance(
        this System.Type typeInstance,
        params dynamic[] constructorArguments
    )
    {
        // Ensure we can create a singleton instance of the type.
        EnsureCanSingleton(typeInstance);

        // Create a new instance of the type and store it in place of any existing one.
        return Replace(typeInstance, () => typeInstance.CreateInstance(constructorArguments));
    }

    /// <summary>
    ///     This method creates a singleton instance of a type.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to create the Singleton for.</param>
    /// <typeparam name="TInstance">The expected type of the instance.</typeparam>
    /// <returns>The singleton instance of <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    // ReSharper disable once UnusedMember.Global
    public static TInstance CreateSingletonInstance<TInstance>(this System.Type typeInstance)
        where TInstance : class => typeInstance.CreateSingletonInstance() as TInstance;

    /// <summary>
    ///     This method creates a singleton instance of a type.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to create the Singleton for.</param>
    /// <param name="constructorArguments">The arguments to pass to the <paramref name="typeInstance" /> constructor.</param>
    /// <typeparam name="TInstance">The expected type of the instance.</typeparam>
    /// <returns>The singleton instance of <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    // ReSharper disable once UnusedMember.Global
    public static TInstance CreateSingletonInstance<TInstance>(
        this System.Type typeInstance,
        params dynamic[] constructorArguments
    )
        where TInstance : class =>
        typeInstance.CreateSingletonInstance(constructorArguments) as TInstance;

    /// <summary>
    ///     This method returns the stored instance of the type, creating and storing one if there is
    ///     none.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to retrieve the Singleton for.</param>
    /// <returns>The singleton instance of <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    // ReSharper disable once MemberCanBePrivate.Global
    public static dynamic GetSingletonInstance(this System.Type typeInstance)
    {
        // Ensure we can create a singleton instance of the type.
        EnsureCanSingleton(typeInstance);

        // We're done, return the singleton instance.
        return GetOrCreate(typeInstance, () => typeInstance.CreateInstance());
    }

    /// <summary>
    ///     This method returns the stored instance of the type, creating one with
    ///     <paramref name="constructorArguments" /> if there is none. The arguments are ignored when
    ///     an instance already exists.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to retrieve the Singleton for.</param>
    /// <param name="constructorArguments">The arguments to pass to the <paramref name="typeInstance" /> constructor.</param>
    /// <returns>The singleton instance of <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    // ReSharper disable once MemberCanBePrivate.Global
    public static dynamic GetSingletonInstance(
        this System.Type typeInstance,
        params dynamic[] constructorArguments
    )
    {
        // Ensure we can create a singleton instance of the type.
        EnsureCanSingleton(typeInstance);

        // We're done, return the singleton instance.
        return GetOrCreate(typeInstance, () => typeInstance.CreateInstance(constructorArguments));
    }

    /// <summary>
    ///     This method retrieves a singleton instance of a type.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to retrieve the Singleton for.</param>
    /// <typeparam name="TInstance">The expected type of the instance.</typeparam>
    /// <returns>The singleton instance of <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    // ReSharper disable once UnusedMember.Global
    public static TInstance GetSingletonInstance<TInstance>(this System.Type typeInstance)
        where TInstance : class => typeInstance.GetSingletonInstance() as TInstance;

    /// <summary>
    ///     This method retrieves a singleton instance of a type.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to retrieve the Singleton for.</param>
    /// <typeparam name="TInstance">The expected type of the instance.</typeparam>
    /// <param name="constructorArguments">The arguments to pass to the <paramref name="typeInstance" /> constructor.</param>
    /// <returns>The singleton instance of <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    /// <exception cref="ArgumentException">When <paramref name="typeInstance" /> is not a class or is abstract.</exception>
    // ReSharper disable once UnusedMember.Global
    public static TInstance GetSingletonInstance<TInstance>(
        this System.Type typeInstance,
        params dynamic[] constructorArguments
    )
        where TInstance : class =>
        typeInstance.GetSingletonInstance(constructorArguments) as TInstance;
}
