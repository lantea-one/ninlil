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
///     <c>{k:v}</c> and <c>(a,b)</c> for lists, dictionaries and tuples. Enums print their
///     description (<c>[EnumMember]</c> value, otherwise the member name) and a class without its own
///     text form prints its properties as <c>{Name:value,...}</c>. It is the inverse of
///     <see cref="Ninlil.Common.Extension.String.GetTypeValueStringExtension" />, which reads the
///     same enum descriptions back, and is used for console output and help text.
/// </summary>
public static class ToStringValueObjectExtension
{
    /// <summary>
    ///     This method converts the <paramref name="dictionary" /> to a <see cref="string" /> value.
    /// </summary>
    /// <param name="dictionary">The <see cref="object" /> instance to convert.</param>
    /// <param name="active">The class instances being rendered, for the cycle guard.</param>
    /// <returns>The <see cref="string" /> representation of the <paramref name="dictionary" />.</returns>
    private static string DictionaryToString(object dictionary, HashSet<object> active) =>
        // Read through the non-generic IDictionary, which every Dictionary<TKey,TValue>
        // implements whatever its key and value types; a cast to Dictionary<object, object> fails
        // because generics are invariant. Iterate Keys plus the indexer rather than
        // Cast<DictionaryEntry>(): Dictionary's non-generic enumerator yields boxed
        // KeyValuePair<TKey,TValue> values, not DictionaryEntry.
        dictionary is IDictionary dict
            ? $"{{{string.Join(",", dict.Keys.Cast<object>().Select(key =>
                string.Join(":", key.ToStringValueCore(active), dict[key].ToStringValueCore(active))))}}}"
            : "{}";

    /// <summary>
    ///     This method converts the <paramref name="list" /> into a <see cref="string"/> value.
    /// </summary>
    /// <param name="list">The list of <see cref="object"/> to convert.</param>
    /// <param name="active">The class instances being rendered, for the cycle guard.</param>
    /// <returns>The <see cref="string" /> representation of the <paramref name="list" />.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when the <paramref name="list" /> is not a recognized type.
    /// </exception>
    private static string ListToString(object list, HashSet<object> active) =>
        string.Join(
            ",",
            list switch
            {
                // Enumerate through the non-generic IEnumerable, which every array and list implements.
                // A value-type collection such as int[] is not covariant to IEnumerable<object>, so
                // matching on the generic interface would miss it.
                IEnumerable l => l.Cast<object>().Select(s => s.ToStringValueCore(active)),

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
    /// <param name="active">The class instances being rendered, for the cycle guard.</param>
    /// <returns>The <see cref="string" /> representation of the <paramref name="tuple" />.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when the <paramref name="tuple" /> is not a recognized type.
    /// </exception>
    private static string TupleToString(object tuple, HashSet<object> active) =>
        // Every Tuple<...> and ValueTuple<...> implements ITuple, which exposes the element count
        // and values by position, so this handles any arity and any element types.
        tuple is ITuple t
            ? $"({string.Join(",", System.Linq.Enumerable.Range(0, t.Length).Select(i => t[i].ToStringValueCore(active)))})"
            : throw new ArgumentException(
                $"Invalid Tuple Type [{tuple.GetType().Name}] Provided",
                nameof(tuple)
            );

    /// <summary>
    ///     This method converts the <see cref="object"/> <paramref name="instance" /> to a
    ///     <see cref="string"/>. Known scalar types use a fixed format; enums print their
    ///     description; collections, dictionaries and tuples are converted item by item. For any
    ///     other object the first rule that applies wins: a custom <see cref="TypeConverter" />
    ///     that converts to a string, an override of <see cref="object.ToString" /> (records,
    ///     <see cref="System.Net.IPAddress" />, <see cref="Uri" />), and otherwise the public
    ///     instance properties as <c>{Name:value,...}</c>. A class that is already being rendered
    ///     further up (a reference cycle) prints as its type name.
    /// </summary>
    /// <param name="instance">The <see cref="object" /> instance to convert.</param>
    /// <returns>The <see cref="string" /> value of the <paramref name="instance" />.</returns>
    public static string ToStringValue(this object instance) =>
        instance.ToStringValueCore(new HashSet<object>(ReferenceEqualityComparer.Instance));

    /// <summary>
    ///     This method is the recursive worker behind <see cref="ToStringValue(object)" />.
    /// </summary>
    /// <param name="instance">The <see cref="object" /> instance to convert.</param>
    /// <param name="active">
    ///     The class instances being rendered on the way down to this call, compared by reference.
    /// </param>
    /// <returns>The <see cref="string" /> value of the <paramref name="instance" />.</returns>
    private static string ToStringValueCore(this object instance, HashSet<object> active) =>
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

            // Check for an enumeration, then convert to its description: the [EnumMember] value
            // when there is one, otherwise the member name. GetTypedValue matches the same
            // descriptions, so the text parses back to the member.
            System.Enum v => v.ToDescription(
                v.GetType(),
                v.ToString(),
                GetEnumDescriptionsTypeExtension.DefaultAttributeChecks
            ),

            // Check for a TimeOnly object, then convert to an ISO-8601 string.
            TimeOnly v => v.ToString("O"),

            // Check for a timespan object, then convert to an ISO-8601 string.
            TimeSpan v => v.ToString("c"),

            // Check for an array or list, then convert to a comma-separated string.
            not null when instance.GetType().IsEnumerableType(false) => ListToString(
                instance,
                active
            ),

            // Check for a dictionary, then convert to a comma-separated string of key-value pairs.
            not null when instance.GetType().IsDictionary() => DictionaryToString(instance, active),

            // Check for a tuple, then convert to a comma-separated string of values.
            not null when instance.GetType().IsTuple() => TupleToString(instance, active),

            // Check for a custom TypeConverter that converts to a string, then use it. The base
            // TypeConverter answers yes for every type and only calls ToString, so it does not
            // count: it would hide the property dictionary for every ordinary class.
            not null
                when TypeDescriptor.GetConverter(instance.GetType()) is var converter
                    && converter.GetType() != typeof(TypeConverter)
                    && converter.CanConvertTo(typeof(string)) => converter.ConvertToString(
                instance
            ),

            // Check for a type that overrides ToString (records, IPAddress, Uri and so on), then
            // use that text.
            not null
                when instance
                    .GetType()
                    .GetMethod(nameof(ToString), System.Type.EmptyTypes)
                    ?.DeclaringType != typeof(object) => instance.ToString(),

            // Check for a class then write its properties as {Name:value,...}. A class that is
            // already being rendered (a reference cycle) is written as its type name, which ends
            // the recursion.
            not null when instance.GetType().IsClass => PropertiesToString(instance, active),

            // Otherwise, call its ToString method (null stays null).
            _ => instance?.ToString(),
        };

    /// <summary>
    ///     This method writes the public instance properties of the class
    ///     <paramref name="instance" /> as <c>{Name:value,...}</c> in declaration order.
    /// </summary>
    /// <param name="instance">The class instance to write.</param>
    /// <param name="active">The class instances being rendered, for the cycle guard.</param>
    /// <returns>
    ///     The property text, or the type name when <paramref name="instance" /> is already being
    ///     rendered further up.
    /// </returns>
    private static string PropertiesToString(object instance, HashSet<object> active)
    {
        // A reference seen again while it is still being rendered would recurse forever.
        if (!active.Add(instance))
            return instance.GetType().Name;

        try
        {
            // Indexers are skipped: reading one needs an index argument and throws without it.
            return $"{{{string.Join(",", instance
                .GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => $"{p.Name}:{p.GetValue(instance).ToStringValueCore(active)}"))}}}";
        }
        finally
        {
            active.Remove(instance);
        }
    }
}
