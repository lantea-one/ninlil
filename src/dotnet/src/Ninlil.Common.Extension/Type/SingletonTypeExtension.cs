// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This static class keeps one process-wide instance per concrete class type, created on
///     demand from a <see cref="System.Type" />. <c>Create*</c> always replaces the stored
///     instance; <c>Get*</c> returns it, creating it first if needed. The store is a plain list
///     with no locking, so it is not safe for concurrent use.
/// </summary>
// ReSharper disable once UnusedType.Global
public static class SingletonTypeExtension
{
    /// <summary>
    ///     This field holds the stored instances, at most one per type.
    /// </summary>
    private static readonly List<dynamic> SingletonInstances = new();

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

        // Remove any existing instances of the type.
        SingletonInstances.RemoveAll(instance => instance.GetType() == typeInstance);

        // Create a new instance of the type.
        dynamic instance = typeInstance.CreateInstance();

        // Add the instance to the list of singleton instances.
        SingletonInstances.Add(instance);

        // Return the instance.
        return instance;
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

        // Remove any existing instances of the type.
        SingletonInstances.RemoveAll(instance => instance.GetType() == typeInstance);

        // Create a new instance of the type.
        dynamic instance = typeInstance.CreateInstance(constructorArguments);

        // Add the instance to the list of singleton instances.
        SingletonInstances.Add(instance);

        // Return the instance.
        return instance;
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
        return SingletonInstances.FirstOrDefault(instance => instance.GetType() == typeInstance)
            ?? typeInstance.CreateSingletonInstance();
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
        return SingletonInstances.FirstOrDefault(instance => instance.GetType() == typeInstance)
            ?? typeInstance.CreateSingletonInstance(constructorArguments);
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
