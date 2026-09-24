using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Object;

/// <summary>
///     This class contains extension methods for copying properties from one object to another of the same type.
/// </summary>
public static class CopyObjectExtension
{
    /// <summary>
    ///     This extension method copies the property values
    ///     from the <paramref name="source" /> to the
    ///     <paramref name="destination" /> instance.
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
        // Reflect over the source value's actual runtime type, not the compile-time TSource type
        // parameter -- when `source` is declared as a looser/base type than what it actually
        // holds at runtime (e.g. a variable typed `object` holding a real model instance), using
        // typeof(TSource) would walk that looser type's own (often far smaller, or in object's
        // case entirely empty) property set instead of the real instance's properties, silently
        // copying nothing.
        foreach (var property in source.GetType().GetProperties().Where(p => p.CanWrite))
            property.SetValue(destination, property.GetValue(source));

        // We're done, return the populated destination instance.
        return destination;
    }

    /// <summary>
    ///     This extension method copies the property values
    ///     from the <paramref name="source" /> to a new instance
    ///     of the <typeparamref name="TDestination" /> type.
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

    // The single-type-parameter same-type convenience overloads that used to live here
    // (`Copy<TClass>(this TClass source, TClass destination)` and
    // `Copy<TClass>(this TClass source, params object[] constructorArguments)`) are gone. Each was
    // a real, confirmed compile-time ambiguity (CS0121) with its two-type-parameter sibling above
    // whenever TSource and TDestination happened to coincide -- the compiler could not choose
    // between them without an explicit `.Copy<TSource, TDestination>(...)` call. They added no
    // capability the two-type-parameter overloads didn't already provide: when TSource and
    // TDestination are the same type, ordinary generic type inference resolves the
    // two-type-parameter overloads to that exact same-type case on its own, so removing the
    // redundant overloads keeps every call shape working (`source.Copy(destination)`,
    // `source.Copy<TClass, TClass>(destination)`, etc.) while eliminating the ambiguity entirely.
}
