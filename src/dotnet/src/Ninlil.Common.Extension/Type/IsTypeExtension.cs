// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This static class classifies a <see cref="System.Type" /> as an array, list, collection,
///     dictionary, enumerable, key/value pair, tuple, nullable or primitive. The string conversion
///     and console parsing helpers use it to pick a conversion.
///     <para>
///         The generic checks compare the type's generic definition against a fixed set
///         (for example <see cref="List{T}" /> and <see cref="IList{T}" /> for a list). They do not
///         match other implementations such as <see cref="HashSet{T}" />. They must not use
///         <c>instance.IsAssignableFrom(typeof(X&lt;&gt;))</c>: that tests assignability the wrong
///         way round and is true for <see cref="object" />.
///     </para>
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
    ///     This method checks if the given <paramref name="instance" /> is a closed
    ///     <see cref="ICollection{T}" /> interface type.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is an <see cref="ICollection{T}" /> or not.</returns>
    public static bool IsCollection(this System.Type instance) =>
        // Compare generic definitions only (see the class summary).
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
    ///     This method checks if the given <paramref name="instance" /> is a
    ///     <see cref="Dictionary{TKey,TValue}" /> or <see cref="IDictionary{TKey,TValue}" /> type.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the type is a <see cref="Dictionary{TKey,TValue}" /> or not.
    /// </returns>
    public static bool IsDictionary(this System.Type instance) =>
        // Compare generic definitions only (see the class summary).
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
    ///     This method checks if the given <paramref name="instance" /> is a closed
    ///     <see cref="IEnumerable{T}" /> interface type.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is an <see cref="IEnumerable{T}" /> or not.</returns>
    public static bool IsEnumerable(this System.Type instance) =>
        // Compare generic definitions only (see the class summary).
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
    ///     This extension method checks if the given <paramref name="instance" /> is an array, or one
    ///     of the list, collection, enumerable or (optionally) dictionary types the other checks in
    ///     this class recognize.
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
    ///     This method checks if the given <paramref name="instance" /> is a <see cref="List{T}" />
    ///     or <see cref="IList{T}" /> type.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is an <see cref="IList{T}" /> or not.</returns>
    public static bool IsList(this System.Type instance) =>
        // Compare generic definitions only (see the class summary).
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
    ///     This extension method checks whether the given <paramref name="instance" /> can hold
    ///     <see langword="null" />: any reference type, or a <see cref="Nullable{T}" />.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is nullable or not.</returns>
    public static bool IsNullable(this System.Type instance) =>
        !instance.IsValueType || Nullable.GetUnderlyingType(instance) is not null;

    /// <summary>
    ///     This method checks if the given <paramref name="instance" /> is a CLR primitive or one of
    ///     the simple value-like types treated the same way: <see cref="string" />,
    ///     <see cref="decimal" />, <see cref="DateTime" />, <see cref="DateTimeOffset" />,
    ///     <see cref="TimeSpan" /> and <see cref="Guid" />.
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
    ///     This field lists every open generic <see cref="System.Tuple" /> and <see cref="ValueTuple" />
    ///     definition, from one to eight elements (the eighth being the nested <c>Rest</c>), for
    ///     <see cref="IsTuple" />.
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
        // Check every arity, not just the single-element Tuple<>/ValueTuple<> definitions, and
        // compare generic definitions only (see the class summary).
        instance.IsGenericType
        && TupleGenericTypeDefinitions.Contains(instance.GetGenericTypeDefinition());
}
