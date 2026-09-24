// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///  This class contains extension methods for checking if a type is something.
/// </summary>
public static class IsTypeExtension
{
    /// <summary>
    ///     This extension method checks if the given <paramref name="instance" /> is an <see cref="Array" /> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is an <see cref="Array" /> or not.</returns>
    public static bool IsArray(this System.Type instance) => instance.IsArray;

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is an
    ///     <see cref="Array" /> of a specific <paramref name="elementType"/> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <param name="elementType">The type of the element in the <see cref="Array" />.</param>
    /// <returns>
    ///     The <see cref="bool" /> indicating whether the type is an
    ///     <see cref="Array" /> of <paramref name="elementType" /> or not.
    /// </returns>
    public static bool IsArray(this System.Type instance, System.Type elementType) =>
        instance.IsArray && instance.GetElementType() == elementType;

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is an
    ///     <see cref="Array" /> of a specific type <typeparamref name="TValue" /> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TValue">The type of the value in the <see cref="Array" />.</typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is an
    ///     <see cref="Array" /> of <typeparamref name="TValue" /> or not.
    /// </returns>
    public static bool IsArray<TValue>(this System.Type instance) =>
        IsArray(instance, typeof(TValue));

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="Array" /> of <see cref="KeyValuePair{TKey,TValue}" /> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TKey">The type of the key in <see cref="KeyValuePair{TKey,TValue}" />.</typeparam>
    /// <typeparam name="TValue">The type of the value in <see cref="KeyValuePair{TKey,TValue}" />.</typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is an <see cref="Array" />
    ///     of the  <see cref="KeyValuePair{TKey,TValue}" /> or not.
    /// </returns>
    public static bool IsArray<TKey, TValue>(this System.Type instance) =>
        IsArray<KeyValuePair<TKey, TValue>>(instance);

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is a <see cref="bool" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is <see cref="bool" /> or not.</returns>
    public static bool IsBoolean(this System.Type instance) => instance == typeof(bool);

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="ICollection{T}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is an <see cref="ICollection{T}" /> or not.</returns>
    public static bool IsCollection(this System.Type instance) =>
        // Only compare against the exact open generic type definition here -- the previous
        // "instance.IsAssignableFrom(typeof(ICollection<>))" fallback checked assignability
        // backwards (whether the unbound, instance-less open generic type itself could be
        // assigned to `instance`), which .NET considers true for `object` and any other
        // sufficiently broad reference type, so it silently misreported those as collections.
        instance.IsGenericType
        && instance.GetGenericTypeDefinition() == typeof(ICollection<>);

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is an
    ///     <see cref="ICollection{T}" /> of a specific <paramref name="valueType"/> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <param name="valueType">The type of the value in the <see cref="ICollection{T}" />.</param>
    /// <returns>
    ///     The <see cref="bool" /> indicating whether the type is an
    ///     <see cref="ICollection{T}" /> of <paramref name="valueType" /> or not.
    /// </returns>
    public static bool IsCollection(this System.Type instance, System.Type valueType) =>
        instance.IsGenericType
        && instance.GetGenericTypeDefinition() == typeof(ICollection<>)
        && instance.GetGenericArguments().Length is 1
        && instance.GetGenericArguments().First() == valueType;

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="ICollection{TValue}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TValue">The type of the value in the collection.</typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is an <see cref="ICollection{TValue}" /> or not.
    /// </returns>
    public static bool IsCollection<TValue>(this System.Type instance) =>
        IsCollection(instance, typeof(TValue));

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="ICollection{KeyValuePair}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TKey">
    ///     The type of the key in <see cref="KeyValuePair{TKey,TValue}"/> the collection.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The type of the value in <see cref="KeyValuePair{TKey,TValue}"/> the collection.
    /// </typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is an
    ///     <see cref="ICollection{KeyValuePair}" /> or not.
    /// </returns>
    public static bool IsCollection<TKey, TValue>(this System.Type instance) =>
        IsCollection<KeyValuePair<TKey, TValue>>(instance);

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is a <see cref="Dictionary{TKey,TValue}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is a <see cref="Dictionary{TKey,TValue}" /> or not.
    /// </returns>
    public static bool IsDictionary(this System.Type instance) =>
        // Only compare against the exact open generic type definitions here -- the previous
        // "instance.IsAssignableFrom(typeof(Dictionary<,>)/typeof(IDictionary<,>))" fallback
        // checked assignability backwards (whether the unbound, instance-less open generic type
        // itself could be assigned to `instance`), which .NET considers true for `object` and any
        // other sufficiently broad reference type, so it silently misreported those as
        // dictionaries.
        instance.IsGenericType
        && (
            instance.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            || instance.GetGenericTypeDefinition() == typeof(IDictionary<,>)
        );

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is an
    ///     <see cref="Dictionary{TKey,TValue}" /> with a specific
    ///     <paramref name="keyType"/> and <paramref name="valueType"/> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <param name="keyType">The type of the key in the <see cref="Dictionary{TKey,TValue}" />.</param>
    /// <param name="valueType">The type of the value in the <see cref="Dictionary{TKey, TValue}" />.</param>
    /// <returns>
    ///     The <see cref="bool" /> indicating whether the type is an
    ///     <see cref="Dictionary{TKey,TValue}" /> with a specific
    ///     <paramref name="keyType"/> and <paramref name="valueType" /> or not.
    /// </returns>
    public static bool IsDictionary(
        this System.Type instance,
        System.Type keyType,
        System.Type valueType
    ) =>
        instance.IsGenericType
        && (
            instance.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            || instance.GetGenericTypeDefinition() == typeof(IDictionary<,>)
        )
        && instance.GetGenericArguments().Length is 2
        && instance.GetGenericArguments().First() == keyType
        && instance.GetGenericArguments().Last() == valueType;

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is a <see cref="Dictionary{TKey,TValue}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TKey">The type of the key in the dictionary.</typeparam>
    /// <typeparam name="TValue">The type of the value in the dictionary.</typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is a <see cref="Dictionary{TKey,TValue}" /> or not.
    /// </returns>
    public static bool IsDictionary<TKey, TValue>(this System.Type instance) =>
        IsDictionary(instance, typeof(TKey), typeof(TValue));

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="IEnumerable{T}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is an <see cref="IEnumerable{T}" /> or not.</returns>
    public static bool IsEnumerable(this System.Type instance) =>
        // Only compare against the exact open generic type definition here -- the previous
        // "instance.IsAssignableFrom(typeof(IEnumerable<>))" fallback checked assignability
        // backwards (whether the unbound, instance-less open generic type itself could be
        // assigned to `instance`), which .NET considers true for `object` and any other
        // sufficiently broad reference type, so it silently misreported those as enumerables.
        instance.IsGenericType
        && instance.GetGenericTypeDefinition() == typeof(IEnumerable<>);

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is an
    ///     <see cref="IEnumerable{T}" /> of a specific <paramref name="valueType"/> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <param name="valueType">The type of the value in the <see cref="IEnumerable{T}" />.</param>
    /// <returns>
    ///     The <see cref="bool" /> indicating whether the type is an
    ///     <see cref="IEnumerable{T}" /> of <paramref name="valueType" /> or not.
    /// </returns>
    public static bool IsEnumerable(this System.Type instance, System.Type valueType) =>
        instance.IsGenericType
        && instance.GetGenericTypeDefinition() == typeof(IEnumerable<>)
        && instance.GetGenericArguments().Length is 1
        && instance.GetGenericArguments().First() == valueType;

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="IEnumerable{TValue}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TValue">The type of the value in the collection.</typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is an <see cref="IEnumerable{TValue}" /> or not.
    /// </returns>
    public static bool IsEnumerable<TValue>(this System.Type instance) =>
        IsEnumerable(instance, typeof(TValue));

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="IEnumerable{KeyValuePair}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TKey">
    ///     The type of the key in <see cref="KeyValuePair{TKey,TValue}"/> the collection.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The type of the value in <see cref="KeyValuePair{TKey,TValue}"/> the collection.
    /// </typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is an
    ///     <see cref="IEnumerable{KeyValuePair}" /> or not.
    /// </returns>
    public static bool IsEnumerable<TKey, TValue>(this System.Type instance) =>
        IsEnumerable<KeyValuePair<TKey, TValue>>(instance);

    /// <summary>
    ///     This extension method checks if the given <paramref name="instance" /> is an enumerable type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <param name="includeDictionary">
    ///     A <see cref="bool" /> indicating whether to include
    ///     <see cref="Dictionary{TKey,TValue}" /> in the check
    ///     or not. If set to <see langword="true" />, the method
    ///     will also check for <see cref="Dictionary{TKey,TValue}" />
    ///     types in addition to other enumerable types.
    /// </param>
    /// <returns>A <see cref="bool" /> indicating whether the type is enumerable or not.</returns>
    public static bool IsEnumerableType(this System.Type instance, bool includeDictionary = true) =>
        instance.IsArray
        || IsArray(instance)
        || IsList(instance)
        || (includeDictionary && IsDictionary(instance))
        || IsCollection(instance)
        || IsEnumerable(instance);

    /// <summary>
    ///     This extension method checks if the given <paramref name="instance" />
    ///     is a <see cref="KeyValuePair{TKey,TValue}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is a <see cref="KeyValuePair{TKey,TValue}" /> or not.
    /// </returns>
    public static bool IsKeyValuePair(this System.Type instance) =>
        instance.IsGenericType && instance.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is an
    ///     <see cref="KeyValuePair{TKey,TValue}" /> with a specific
    ///     <paramref name="keyType"/> and <paramref name="valueType"/> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <param name="keyType">The type of the key in the <see cref="KeyValuePair{TKey,TValue}" />.</param>
    /// <param name="valueType">The type of the value in the <see cref="KeyValuePair{TKey, TValue}" />.</param>
    /// <returns>
    ///     The <see cref="bool" /> indicating whether the type is an
    ///     <see cref="KeyValuePair{TKey,TValue}" /> with a specific
    ///     <paramref name="keyType"/> and <paramref name="valueType" /> or not.
    /// </returns>
    public static bool IsKeyValuePair(
        this System.Type instance,
        System.Type keyType,
        System.Type valueType
    ) =>
        instance.IsGenericType
        && instance.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
        && instance.GetGenericArguments().Length is 2
        && instance.GetGenericArguments().First() == keyType
        && instance.GetGenericArguments().Last() == valueType;

    /// <summary>
    ///     This extension method checks if the given <paramref name="instance" />
    ///     is a <see cref="KeyValuePair{TKey,TValue}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TKey">The type of the key in the <see cref="KeyValuePair{TKey,TValue}" />.</typeparam>
    /// <typeparam name="TValue">The type of the value in the <see cref="KeyValuePair{TKey,TValue}" />.</typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is a <see cref="KeyValuePair{TKey,TValue}" /> or not.
    /// </returns>
    public static bool IsKeyValuePair<TKey, TValue>(this System.Type instance) =>
        IsKeyValuePair(instance, typeof(TKey), typeof(TValue));

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="IList{T}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is an <see cref="IList{T}" /> or not.</returns>
    public static bool IsList(this System.Type instance) =>
        // Only compare against the exact open generic type definitions here -- the previous
        // "instance.IsAssignableFrom(typeof(List<>)/typeof(IList<>))" fallback checked
        // assignability backwards (whether the unbound, instance-less open generic type itself
        // could be assigned to `instance`), which .NET considers true for `object` and any other
        // sufficiently broad reference type, so it silently misreported those as lists.
        instance.IsGenericType
        && (
            instance.GetGenericTypeDefinition() == typeof(List<>)
            || instance.GetGenericTypeDefinition() == typeof(IList<>)
        );

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is an
    ///     <see cref="IList{T}" /> of a specific <paramref name="valueType"/> or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <param name="valueType">The type of the value in the <see cref="IList{T}" />.</param>
    /// <returns>
    ///     The <see cref="bool" /> indicating whether the type is an
    ///     <see cref="IList{T}" /> of <paramref name="valueType" /> or not.
    /// </returns>
    public static bool IsList(this System.Type instance, System.Type valueType) =>
        instance.IsGenericType
        && (
            instance.GetGenericTypeDefinition() == typeof(List<>)
            || instance.GetGenericTypeDefinition() == typeof(IList<>)
        )
        && instance.GetGenericArguments().Length is 1
        && instance.GetGenericArguments().First() == valueType;

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="IList{TValue}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TValue">The type of the value in the collection.</typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is an <see cref="IList{TValue}" /> or not.
    /// </returns>
    public static bool IsList<TValue>(this System.Type instance) =>
        IsList(instance, typeof(TValue));

    /// <summary>
    ///     This method checks if the given <paramref name="instance" />
    ///     is an <see cref="IList{KeyValuePair}" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <typeparam name="TKey">
    ///     The type of the key in <see cref="KeyValuePair{TKey,TValue}"/> the collection.
    /// </typeparam>
    /// <typeparam name="TValue">
    ///     The type of the value in <see cref="KeyValuePair{TKey,TValue}"/> the collection.
    /// </typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is an
    ///     <see cref="IList{KeyValuePair}" /> or not.
    /// </returns>
    public static bool IsList<TKey, TValue>(this System.Type instance) =>
        IsList<KeyValuePair<TKey, TValue>>(instance);

    /// <summary>
    ///     This extension method checks if the given <paramref name="instance" /> is a nullable type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is nullable or not.</returns>
    public static bool IsNullable(this System.Type instance) =>
        !instance.IsValueType || Nullable.GetUnderlyingType(instance) is not null;

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is a primitive type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is primitive or not.</returns>
    public static bool IsPrimitive(this System.Type instance) =>
        instance.IsPrimitive
        || instance == typeof(string)
        || instance == typeof(decimal)
        || instance == typeof(DateTime)
        || instance == typeof(DateTimeOffset)
        || instance == typeof(TimeSpan)
        || instance == typeof(Guid);

    /// <summary>
    ///     This field enumerates every open generic <see cref="Tuple" />/<see cref="ValueTuple" />
    ///     type definition, across their full 1-through-8-element (<c>Rest</c>-nested) arity range, used
    ///     by <see cref="IsTuple" /> to recognize any real tuple shape rather than just a single element.
    /// </summary>
    private static readonly System.Type[] TupleGenericTypeDefinitions =
    [
        typeof(Tuple<>),
        typeof(Tuple<,>),
        typeof(Tuple<,,>),
        typeof(Tuple<,,,>),
        typeof(Tuple<,,,,>),
        typeof(Tuple<,,,,,>),
        typeof(Tuple<,,,,,,>),
        typeof(Tuple<,,,,,,,>),
        typeof(ValueTuple<>),
        typeof(ValueTuple<,>),
        typeof(ValueTuple<,,>),
        typeof(ValueTuple<,,,>),
        typeof(ValueTuple<,,,,>),
        typeof(ValueTuple<,,,,,>),
        typeof(ValueTuple<,,,,,,>),
        typeof(ValueTuple<,,,,,,,>),
    ];

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is a <see langword="Tuple" /> type or not.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is a <see langword="Tuple" /> or not.</returns>
    public static bool IsTuple(this System.Type instance) =>
        // Compare against every 1-through-8-element Tuple/ValueTuple open generic type definition,
        // not just the single-element Tuple<>/ValueTuple<> forms -- the previous version's
        // GetGenericTypeDefinition() comparison only ever matched a single-element tuple, so every
        // real-world multi-element Tuple<T1,T2,...> went undetected. The previous
        // "instance.IsAssignableFrom(typeof(Tuple<>)/typeof(ValueTuple<>))" fallback is also removed
        // for the same reason as the other IsX checks in this class: it checked assignability
        // backwards and incorrectly matched broad reference types like `object`.
        instance.IsGenericType
        && TupleGenericTypeDefinitions.Contains(instance.GetGenericTypeDefinition());
}
