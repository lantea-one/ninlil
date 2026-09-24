using System.Reflection;

// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This class maintains our CreateInstance extensions for <see cref="System.Type" /> objects.
/// </summary>
public static class CreateInstanceTypeExtension
{
    /// <summary>
    ///     This method creates an instance of the <see cref="System.Type" /> the <paramref name="typeInstance" /> represents.
    /// </summary>
    /// <param name="typeInstance">The type of instance to create.</param>
    /// <returns>A new instance representative of the <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    public static dynamic CreateInstance(this System.Type typeInstance) =>
        typeInstance.GetConstructor(
            BindingFlags.Public | BindingFlags.Instance,
            null,
            System.Type.EmptyTypes,
            null
        )
            is null
            ? typeInstance.GetDefaultValue()
            : Activator.CreateInstance(typeInstance);

    /// <summary>
    ///     This method creates an instance of the <see cref="System.Type" /> the <paramref name="typeInstance" /> represents.
    /// </summary>
    /// <param name="typeInstance">The type of instance to create.</param>
    /// <typeparam name="TInstance">The type of instance to create.</typeparam>
    /// <returns>A new instance representative of the <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    // ReSharper disable once UnusedMember.Global
    public static TInstance CreateInstance<TInstance>(this System.Type typeInstance) =>
        (TInstance)typeInstance.CreateInstance();

    /// <summary>
    ///     This method creates an instance of the <see cref="System.Type" /> the <paramref name="typeInstance" /> represents.
    /// </summary>
    /// <param name="typeInstance">The type of instance to create.</param>
    /// <param name="constructorArguments">The arguments to pass to the constructor.</param>
    /// <returns>A new instance representative of the <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    public static dynamic CreateInstance(
        this System.Type typeInstance,
        params dynamic[] constructorArguments
    ) =>
        constructorArguments.Length > 0
            ? Activator.CreateInstance(typeInstance, constructorArguments)
            : typeInstance.CreateInstance();

    /// <summary>
    ///     This method creates an instance of the <see cref="System.Type" /> the <paramref name="typeInstance" /> represents.
    /// </summary>
    /// <param name="typeInstance">The type of instance to create.</param>
    /// <param name="constructorArguments">The arguments to pass to the constructor.</param>
    /// <typeparam name="TInstance">The type of instance to create.</typeparam>
    /// <returns>A new instance representative of the <see cref="System.Type" /> <paramref name="typeInstance" />.</returns>
    /// // ReSharper disable once UnusedMember.Global
    public static TInstance CreateInstance<TInstance>(
        this System.Type typeInstance,
        params dynamic[] constructorArguments
    ) =>
        (TInstance)(
            constructorArguments.Length > 0
                ? typeInstance.CreateInstance(constructorArguments)
                : typeInstance.CreateInstance()
        );
}
