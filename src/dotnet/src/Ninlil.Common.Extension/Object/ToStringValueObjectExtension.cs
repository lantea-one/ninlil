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
///     This class maintains our extension methods for converting an <see cref="object" /> to a <see cref="string" />.
/// </summary>
public static class ToStringValueObjectExtension
{
    /// <summary>
    ///     This method converts the <paramref name="dictionary" /> to a <see cref="string" /> value.
    /// </summary>
    /// <param name="dictionary">The <see cref="object" /> instance to convert.</param>
    /// <returns>The <see cref="string" /> representation of the <paramref name="dictionary" />.</returns>
    private static string DictionaryToString(object dictionary) =>
        // Read through the non-generic System.Collections.IDictionary interface, which every
        // closed Dictionary<TKey,TValue> implements regardless of its actual key/value types,
        // instead of the invariant "as Dictionary<object, object>" cast this previously used --
        // generics are invariant, so that cast silently failed (and rendered an empty "{}") for
        // any dictionary whose key/value types were not literally object. Iterating via
        // IDictionary.Keys plus its indexer (rather than Cast<DictionaryEntry>() over the plain
        // enumerator) is required because Dictionary<TKey,TValue>'s own non-generic
        // IEnumerator.Current explicitly yields a boxed KeyValuePair<TKey,TValue>, not a
        // DictionaryEntry -- only its separate IDictionaryEnumerator.Entry property does that.
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
                // Enumerate through the non-generic System.Collections.IEnumerable interface
                // (which every array/list implements regardless of its element type) instead of
                // matching only the specific reference-type-element shapes object[]/List<object>/
                // IEnumerable<object> -- a value-type element array/list (e.g. int[]) is not
                // covariant to IEnumerable<object>, so it previously never matched any case here
                // and fell straight through to the exception below.
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
        // Every real Tuple<...>/ValueTuple<...> arity implements the framework's own
        // System.Runtime.CompilerServices.ITuple interface, exposing its element count and values
        // positionally -- using that here (instead of pattern-matching against the specific,
        // all-object-typed shapes Tuple<object,object>, Tuple<object,object,object>, etc.) means
        // this correctly handles a tuple whose element types are anything at all, not just object,
        // which is virtually every real-world tuple usage.
        tuple is ITuple t
            ? $"({string.Join(",", System.Linq.Enumerable.Range(0, t.Length).Select(i => t[i].ToStringValue()))})"
            : throw new ArgumentException(
                $"Invalid Tuple Type [{tuple.GetType().Name}] Provided",
                nameof(tuple)
            );

    /// <summary>
    ///     This method converts the <see cref="object"/> <paramref name="instance" /> to a <see cref="string"/> value.
    /// </summary>
    /// <param name="instance">The <see cref="object" /> instance to convert.</param>
    /// <returns>The <see cref="string" /> value of the <paramref name="instance" />.</returns>
    public static string ToStringValue(this object instance) =>
        instance switch
        {
            // Check for a string object, then return it as is.
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

            // Check for an enumeration, then convert to a string.
            System.Enum v => v.ToDescription(),

            // Check for a TimeOnly object, then convert to an ISO-8601 string.
            TimeOnly v => v.ToString("O"),

            // Check for a timespan object, then convert to an ISO-8601 string.
            TimeSpan v => v.ToString("c"),

            // Check for an array, then convert to a comma-separated string.
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

            // Otherwise, call it's ToString method.
            _ => instance?.ToString(),
        };
}
