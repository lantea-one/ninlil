using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Object;

/// <summary>
///     This static class copies public property values from one object onto another object of the
///     same type or a derived type. It is a shallow copy: reference-typed values are shared.
/// </summary>
public static class CopyObjectExtension
{
    /// <summary>
    ///     This extension method copies every public writable property value of the
    ///     <paramref name="source" />'s runtime type onto the <paramref name="destination" />.
    /// </summary>
    /// <param name="source">The source object from which to copy properties.</param>
    /// <param name="destination">The destination object to which properties will be copied.</param>
    /// <typeparam name="TSource">The type of the source object from which properties will be copied.</typeparam>
    /// <typeparam name="TDestination">
    ///     The type of the <paramref name="destination" /> object to which properties will be copied.
    /// </typeparam>
    /// <returns>The populated <paramref name="destination" /> instance.</returns>
    public static TDestination Copy<TSource, TDestination>(
        this TSource source,
        TDestination destination
    )
        where TSource : class
        where TDestination : class, TSource
    {
        // Reflect over the runtime type, not TSource. When the source is declared as a base type
        // (or object), typeof(TSource) would list fewer properties, or none, and copy nothing.
        foreach (var property in source.GetType().GetProperties().Where(p => p.CanWrite))
            property.SetValue(destination, property.GetValue(source));

        // We're done, return the populated destination instance.
        return destination;
    }

    /// <summary>
    ///     This extension method creates a new <typeparamref name="TDestination" /> and copies the
    ///     property values from the <paramref name="source" /> onto it.
    /// </summary>
    /// <param name="source">The source object from which to copy properties.</param>
    /// <param name="constructorArguments">The constructor arguments to use when creating the new instance.</param>
    /// <typeparam name="TSource">The type of the source object from which properties will be copied.</typeparam>
    /// <typeparam name="TDestination">
    ///     The type of the destination object to which properties will be copied.
    /// </typeparam>
    /// <returns>The populated <typeparamref name="TDestination"/> instance.</returns>
    public static TDestination Copy<TSource, TDestination>(
        this TSource source,
        params object[] constructorArguments
    )
        where TSource : class
        where TDestination : class, TSource =>
        Copy(source, typeof(TDestination).CreateInstance<TDestination>(constructorArguments));

    // Do not add single-type-parameter overloads such as Copy<TClass>(TClass, TClass). They are
    // ambiguous (CS0121) with the overloads above when both type parameters are the same type,
    // and type inference already handles the same-type case.
}
