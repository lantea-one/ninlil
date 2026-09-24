// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This class maintains the Singleton extensions for <see cref="System.Type" /> objects.
/// </summary>
// ReSharper disable once UnusedType.Global
public static class SingletonTypeExtension
{
    /// <summary>
    ///     This property contains our singleton instances
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
    ///     This method creates a singleton instance of a type.
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
    /// <param name="constructorArguments">The arguments to pass to the <paramref name="typeInstance" /> constuctor.</param>
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
    /// <param name="constructorArguments">The arguments to pass to the <paramref name="typeInstance" /> constuctor.</param>
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
    ///     This method retrieves a singleton instance of a type.
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
    ///     This method retrieves a singleton instance of a type.
    /// </summary>
    /// <param name="typeInstance">The <see cref="System.Type" /> to retrieve the Singleton for.</param>
    /// <param name="constructorArguments">The arguments to pass to the <paramref name="typeInstance" /> constuctor.</param>
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
    /// <param name="constructorArguments">The arguments to pass to the <paramref name="typeInstance" /> constuctor.</param>
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
