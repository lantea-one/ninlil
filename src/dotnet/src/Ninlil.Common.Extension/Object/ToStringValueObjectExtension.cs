using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Ninlil.Common.Extension.Enum;
using Ninlil.Common.Extension.Type;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Extension.Object;

/// <summary>
///     This static class converts any value to a stable, culture-free string: lowercase booleans
///     and GUIDs, ISO 8601 dates, Base64 byte arrays, enum member names, and <c>a,b</c>,
///     <c>{k:v}</c> and <c>(a,b)</c> for lists, dictionaries and tuples. It is the inverse of
///     <see cref="Ninlil.Common.Extension.String.GetTypeValueStringExtension" /> and is used for console
///     output and help text.
/// </summary>
public static class ToStringValueObjectExtension
{
    /// <summary>
    ///     This method converts the <paramref name="dictionary" /> to a <see cref="string" /> value.
    /// </summary>
    /// <param name="dictionary">The <see cref="object" /> instance to convert.</param>
    /// <returns>The <see cref="string" /> representation of the <paramref name="dictionary" />.</returns>
    private static string DictionaryToString(object dictionary) =>
        // Read through the non-generic IDictionary, which every Dictionary<TKey,TValue>
        // implements whatever its key and value types; a cast to Dictionary<object, object> fails
        // because generics are invariant. Iterate Keys plus the indexer rather than
        // Cast<DictionaryEntry>(): Dictionary's non-generic enumerator yields boxed
        // KeyValuePair<TKey,TValue> values, not DictionaryEntry.
        dictionary is IDictionary dict
            ? $"{{{string.Join(",", dict.Keys.Cast<object>().Select(key =>
                string.Join(":", key.ToStringValue(), dict[key].ToStringValue())))}}}"
            : "{}";

    /// <summary>
    ///     This method converts the <paramref name="list" /> into a <see cref="string"/> value.
    /// </summary>
    /// <param name="list">The list of <see cref="object"/> to convert.</param>
    /// <returns>The <see cref="string" /> representation of the <paramref name="list" />.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when the <paramref name="list" /> is not a recognized type.
    /// </exception>
    private static string ListToString(object list) =>
        string.Join(
            ",",
            list switch
            {
                // Enumerate through the non-generic IEnumerable, which every array and list implements.
                // A value-type collection such as int[] is not covariant to IEnumerable<object>, so
                // matching on the generic interface would miss it.
                IEnumerable l => l.Cast<object>().Select(s => s.ToStringValue()),

                // If we don't recognize the type, throw an exception.
                _ => throw new ArgumentException(
                    $"Invalid List Type [{list.GetType().Name}] Provided",
                    nameof(list)
                ),
            }
        );

    /// <summary>
    ///     This method converts the <paramref name="tuple" /> into a <see cref="string"/> value.
    /// </summary>
    /// <param name="tuple">The tuple of <see cref="object"/> to convert.</param>
    /// <returns>The <see cref="string" /> representation of the <paramref name="tuple" />.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when the <paramref name="tuple" /> is not a recognized type.
    /// </exception>
    private static string TupleToString(object tuple) =>
        // Every Tuple<...> and ValueTuple<...> implements ITuple, which exposes the element count
        // and values by position, so this handles any arity and any element types.
        tuple is ITuple t
            ? $"({string.Join(",", System.Linq.Enumerable.Range(0, t.Length).Select(i => t[i].ToStringValue()))})"
            : throw new ArgumentException(
                $"Invalid Tuple Type [{tuple.GetType().Name}] Provided",
                nameof(tuple)
            );

    /// <summary>
    ///     This method converts the <see cref="object"/> <paramref name="instance" /> to a
    ///     <see cref="string"/>. Known scalar types use a fixed format; collections, dictionaries and
    ///     tuples are converted item by item; everything else goes through its
    ///     <see cref="TypeConverter" />. The base converter can always convert to a string (it calls
    ///     <see cref="object.ToString" />), so an ordinary class gets its <c>ToString()</c> and the
    ///     property-dictionary branch after it is only reached when a custom converter refuses.
    /// </summary>
    /// <param name="instance">The <see cref="object" /> instance to convert.</param>
    /// <returns>The <see cref="string" /> value of the <paramref name="instance" />.</returns>
    public static string ToStringValue(this object instance) =>
        instance switch
        {
            // Check for a string object, then return it trimmed.
            string v => v.Trim(),

            // Check for a boolean, then convert to a string.
            bool v => v.ToString().ToLower(),

            // Check for a single byte then convert to a hexadecimal string.
            byte v => v.ToString("X2"),

            // Check for a byte array, then convert to a base64 string.
            byte[] v => Convert.ToBase64String(v),

            // Check for a char, then convert to a string.
            char v => v.ToString(),

            // Check for one of our structured string models, then convert to a string using the model's ToString method.
            StructuredStringModel v => v.ToString(),

            // Check for a date only object, then convert to an ISO-8601 string.
            DateOnly v => v.ToString("O"),

            // Check for DateTime object, then convert to an ISO-8601 string.
            DateTime v => v.ToString("O"),

            // Check for DateTimeOffset object, then convert to an ISO-8601 string.
            DateTimeOffset v => v.ToString("O"),

            // Check for a Guid object, then convert to a string.
            Guid v => v.ToString().ToLower(),

            // Check for an enumeration, then convert to a string. With TEnum bound to System.Enum and no
            // attribute checks, ToDescription returns the member name.
            System.Enum v => v.ToDescription(),

            // Check for a TimeOnly object, then convert to an ISO-8601 string.
            TimeOnly v => v.ToString("O"),

            // Check for a timespan object, then convert to an ISO-8601 string.
            TimeSpan v => v.ToString("c"),

            // Check for an array or list, then convert to a comma-separated string.
            not null when instance.GetType().IsEnumerableType(false) => ListToString(instance),

            // Check for a dictionary, then convert to a comma-separated string of key-value pairs.
            not null when instance.GetType().IsDictionary() => DictionaryToString(instance),

            // Check for a tuple, then convert to a comma-separated string of values.
            not null when instance.GetType().IsTuple() => TupleToString(instance),

            // Check for a type that has a TypeConverter, then convert to a string using the TypeConverter.
            not null
                when TypeDescriptor.GetConverter(instance.GetType()).CanConvertTo(typeof(string)) =>
                TypeDescriptor.GetConverter(instance.GetType()).ConvertToString(instance),

            // Check for a class then convert the properties and values to a dictionary and then to a string.
            not null when instance.GetType().IsClass => new Dictionary<string, object>(
                instance
                    .GetType()
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(p => new KeyValuePair<string, object>(p.Name, p.GetValue(instance)))
            ).ToStringValue(),

            // Otherwise, call its ToString method (null stays null).
            _ => instance?.ToString(),
        };
}
