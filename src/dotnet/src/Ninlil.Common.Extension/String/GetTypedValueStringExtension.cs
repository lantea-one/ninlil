using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Ninlil.Common.Extension.Type;
using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Enum;
using Ninlil.Common.Serializer.Model;
using Ninlil.Common.Serializer.Service;

// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This class provides extension methods for converting a <see cref="string" /> value to typed values.
/// </summary>
public static class GetTypeValueStringExtension
{
    /// <summary>
    ///     This method checks if the <see cref="string" /> <paramref name="instance" />
    ///     can be converted to the <paramref name="type" /> typed value.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> value to check.</param>
    /// <param name="type">The expected resulting type.</param>
    /// <param name="delimiter">Optional delimiter used for parsing collections.</param>
    /// <param name="format">Optional serialization format used.</param>
    /// <returns>A <see cref="bool" /> indicating if the conversion is possible.</returns>
    public static bool CanGetTypedValue(
        this string instance,
        System.Type type,
        string delimiter = ",",
        SerializerFormatEnum format = SerializerFormatEnum.None
    ) => instance.TryGetTypedValue(type, out dynamic _, delimiter, format);

    /// <summary>
    ///     This method checks if the <see cref="string" /> <paramref name="instance" />
    ///     can be converted to the <typeparamref name="TValue" /> typed value.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> value to check.</param>
    /// <param name="delimiter">Optional delimiter used for parsing collections.</param>
    /// <param name="format">Optional serialization format used.</param>
    /// <typeparam name="TValue">The expected resulting type.</typeparam>
    /// <returns>A <see cref="bool" /> indicating if the conversion is possible.</returns>
    public static bool CanGetTypedValue<TValue>(
        this string instance,
        string delimiter = ",",
        SerializerFormatEnum format = SerializerFormatEnum.None
    ) => instance.TryGetTypedValue(out TValue _, delimiter, format);

    /// <summary>
    ///     This method converts the <see cref="string" />
    ///     <paramref name="instance" /> value string to it's <paramref name="type" />.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> value to convert.</param>
    /// <param name="type">The expected resulting type.</param>
    /// <param name="delimiter">Optional, delimiter used for parsing.</param>
    /// <param name="format">Optional, serialization format used.</param>
    /// <returns>Typed representation of <paramref name="instance" /> into <paramref name="type" />.</returns>
    public static object GetTypedValue(
        this string instance,
        System.Type type,
        string delimiter = ",",
        SerializerFormatEnum format = SerializerFormatEnum.None
    )
    {
        // Sanitize the instance.
        instance = instance?.SanitizeForValueTyping();

        // Check for a null instance then return the null or default value.
        if (instance?.Trim().ToLower() is null or "" or "nil" or "null")
            return Nullable.GetUnderlyingType(type) is null ? type.GetDefaultValue() : null;

        // Check for a string and simply return the instance.
        if (type == typeof(string))
            return instance;

        // Check for a serializer format then try to deserialize the value and return it.
        if (format is not SerializerFormatEnum.None)
            return SerializerService.TryDeserialize(type, instance, out object result, format)
                    ? result
                : Nullable.GetUnderlyingType(type) is null ? type.GetDefaultValue()
                : null;

        // Check for an enumerated value then parse and return the value.
        if (type.IsEnum)
            return System.Enum.TryParse(type, instance, true, out object result)
                ? result
                : instance.ToEnum(type);

        // Check for a boolean type then parse and return the value.
        if (type.IsBoolean())
            return Regex.IsMatch(
                instance,
                "on|true|yes|t|y|1",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            );

        // Check for one of our structured string models then parse and return the value.
        if (type.IsAssignableTo(typeof(IStructuredStringModel)))
            return
                StructuredStringModel.TryParse(
                    type,
                    instance,
                    out IStructuredStringModel structured
                ) && structured?.IsNull() is false
                ? structured
                : null;

        // Check for a byte type then parse and return the value.
        if (type == typeof(byte))
            return byte.Parse(instance, NumberStyles.HexNumber);

        // Check for a byte array type then parse and return the value.
        if (type == typeof(byte[]))
            return Convert.FromBase64String(instance);

        // Check for a char type then parse and return the value.
        if (type == typeof(char))
            return char.Parse(instance);

        // Check for a char array type then parse and return the value.
        if (type == typeof(char[]))
            return instance.ToCharArray();

        // Check for a date only then parse and return the value.
        if (type == typeof(DateOnly))
            return DateOnly.Parse(instance);

        // Check for a date/time then parse and return the value.
        if (type == typeof(DateTime))
            return DateTime.Parse(instance);

        // Check for a date/time offset then parse and return the value.
        if (type == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(instance);

        // Check for a decimal then parse and return the value.
        if (type == typeof(decimal))
            return decimal.Parse(instance);

        // Check for a double then parse and return the value.
        if (type == typeof(double))
            return double.Parse(instance);

        // Check for a float then parse and return the value.
        if (type == typeof(float))
            return float.Parse(instance);

        // Check for a Guid then parse and return the value.
        if (type == typeof(Guid))
            return Guid.Parse(instance);

        // Check for an integer then parse and return the value.
        if (type == typeof(int))
            return int.Parse(instance);

        // Check for an IPAddress then parse and return the value.
        if (type == typeof(System.Net.IPAddress))
            return System.Net.IPAddress.Parse(instance);

        // Check for a long then parse and return the value.
        if (type == typeof(long))
            return long.Parse(instance);

        // Check for an EmailAddress then parse and return the value.
        if (type == typeof(MailAddress))
            return new MailAddress(instance);

        // Check for a short then parse and return the value.
        if (type == typeof(short))
            return short.Parse(instance);

        // Check for a TimeOnly then parse and return the value.
        if (type == typeof(TimeOnly))
            return TimeOnly.Parse(instance);

        // Check for TimeSpan then parse and return the value.
        if (type == typeof(TimeSpan))
            return TimeSpan.Parse(instance);

        // Check for an unsigned integer then parse and return the value.
        if (type == typeof(uint))
            return uint.Parse(instance);

        // Check for an unsigned long then parse and return the value.
        if (type == typeof(ulong))
            return ulong.Parse(instance);

        // Check for an unsigned short then parse and return the value.
        if (type == typeof(ushort))
            return ushort.Parse(instance);

        // Check for an array type then parse and return the value.
        if (type.IsEnumerableType(false))
        {
            // Instantiate our new list with the specified element type.
            object instanceList = typeof(List<>)
                .MakeGenericType(
                    type.GetEnumerableElementType()
                        ?? throw new ArgumentException(
                            $"Unable to determine type for list: {type.Name}.",
                            nameof(type)
                        )
                )
                .CreateInstance();

            // Iterate over the split instance and add each typed value to the list.
            instance
                .Split(delimiter, StringSplitOptions.TrimEntries)
                .ToList()
                .ForEach(i =>
                    instanceList
                        .GetType()
                        .CallMethod(
                            instanceList,
                            "Add",
                            i.SanitizeForValueTyping()
                                .GetTypedValue(type.GetEnumerableElementType(), delimiter, format)
                        )
                );

            // Check for an array type then convert the list to an array.
            if (type.IsArray())
                return instanceList.GetType().CallMethod(instanceList, "ToArray")
                    ?? throw new ArgumentException(
                        $"Unable to convert list to array for type: {type.Name}.",
                        nameof(type)
                    );

            // We're done, return the list instance.
            return instanceList;
        }

        // Check for a dictionary type then parse and return the value.
        if (type.IsDictionary())
        {
            // Check for a null instance then return the null or default value.
            if (instance.ToLower() is null or "" or "null" or "{null}")
                return null;

            // Check for an empty dictionary then return an empty dictionary.
            if (instance.ToLower() is "{}" or "new" or "{new}")
                return type.CreateInstance();

            // Localize the key type into this scope.
            System.Type keyType =
                type.GetDictionaryKeyType()
                ?? throw new ArgumentException(
                    $"Unable to determine type for dictionary key: {type.Name}.",
                    nameof(type)
                );

            // Localize the value type into this scope.
            System.Type valueType =
                type.GetDictionaryValueType()
                ?? throw new ArgumentException(
                    $"Unable to determine type for dictionary value: {type.Name}.",
                    nameof(type)
                );

            // Ensure the syntax is correct for the dictionary.
            if (!instance.StartsWith('{') || !instance.EndsWith('}'))
                throw new ArgumentException(
                    $"Invalid dictionary syntax for type: {type.Name}. Expected format: {{<key1>:<value1>,<key2>:<value2>...}}.",
                    nameof(instance)
                );

            // Instantiate a real Dictionary<keyType, valueType> via reflection and populate it
            // through its own "Add" method, rather than LINQ's ToDictionary(keySelector,
            // valueSelector) -- that overload infers TKey/TValue from the selectors' *return*
            // type, which is always `object` here (since the non-generic GetTypedValue(Type, ...)
            // always returns object regardless of the requested key/value types), so it would
            // always produce a Dictionary<object,object> instead of the requested shape.
            object instanceDictionary = typeof(Dictionary<,>)
                .MakeGenericType(keyType, valueType)
                .CreateInstance();

            instance
                .SanitizeForValueTypingDictionary()
                .Split(delimiter, StringSplitOptions.TrimEntries)
                .Select(x => x.Split(':', 2, StringSplitOptions.TrimEntries))
                .ToList()
                .ForEach(kvp =>
                    instanceDictionary
                        .GetType()
                        .CallMethod(
                            instanceDictionary,
                            "Add",
                            kvp.First().GetTypedValue(keyType, delimiter, format),
                            kvp.Last().GetTypedValue(valueType, delimiter, format)
                        )
                );

            // We're done, return the populated, correctly-typed dictionary instance.
            return instanceDictionary;
        }

        // Check for a tuple type then parse and return the value.
        if (type.IsTuple())
        {
            // Check for a null instance then return the null or default value.
            if (instance.ToLower() is null or "" or "null")
                return null;

            // Ensure the syntax is correct for the dictionary.
            if (!instance.StartsWith('(') || !instance.EndsWith(')'))
                throw new ArgumentException(
                    $"Invalid tuple syntax for type: {type.Name}. Expected format: (<item1>,<item2>,<item3>...).",
                    nameof(instance)
                );

            // Localize the element types for the tuple into this scope.
            System.Type[] elementTypes = type.GetTupleElementTypes();

            // Ensure we have at least two element types for a dictionary.
            if (elementTypes.Length < 2)
                throw new ArgumentException(
                    $"Unable to determine element types for tuple: {type.Name}. Expected at least two element types.",
                    nameof(type)
                );

            // Ensure the number of elements matches the expected types. This must split on the
            // real `delimiter` parameter, not a hardcoded comma -- a caller using a non-default
            // delimiter (e.g. Ninlil.Common.Console's tuple parsing, which deliberately uses
            // "]-[" so a comma inside an individual element's own value never gets mistaken for
            // an element separator) would otherwise almost always fail this count check and throw,
            // even for perfectly well-formed input.
            if (
                instance
                    .SanitizeForValueTypingTuple()
                    .Split(delimiter, StringSplitOptions.TrimEntries)
                    .Length != elementTypes.Length
            )
                throw new ArgumentException(
                    string.Format(
                        "Invalid number of elements for tuple: {0}. Expected {1} elements. Received: {2}.",
                        type.Name,
                        elementTypes.Length,
                        instance
                            .SanitizeForValueTypingTuple()
                            .Split(delimiter, StringSplitOptions.TrimEntries)
                            .Length
                    ),
                    nameof(instance)
                );

            // Parse each element per its own tuple-slot type, then construct the requested
            // Tuple/ValueTuple instance via reflection with the parsed values as constructor
            // arguments (every Tuple<...>/ValueTuple<...> arity has a public constructor taking
            // exactly its element values in order). Returning the raw LINQ projection here (as an
            // earlier version of this method did) would produce an untyped IEnumerable<object>,
            // never an actual instance of the requested tuple type.
            return type.CreateInstance(
                instance
                    .SanitizeForValueTypingTuple()
                    .Split(delimiter, StringSplitOptions.TrimEntries)
                    .Select((x, i) => x.GetTypedValue(elementTypes[i], delimiter, format))
                    .ToArray()
            );
        }

        // Try to parse the instance as a JSON object and return the value.
        if (JsonSerializer.TryDeserialize(type, instance, out object json))
            return json;

        // Try to parse the instance as an XML object and return the value.
        if (XmlSerializer.TryDeserialize(type, instance, out object xml))
            return xml;

        // Otherwise, try and change the type then return the value.
        return type == typeof(object) ? instance : Convert.ChangeType(instance.Trim(), type);
    }

    /// <summary>
    ///     This method converts the <see cref="string" />
    ///     <paramref name="instance" /> value string to it's <typeparamref name="TValue" />.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> value to convert.</param>
    /// <param name="delimiter">Optional, delimiter used for parsing.</param>
    /// <param name="format">Optional, serialization format used.</param>
    /// <typeparam name="TValue">The expected resulting type.</typeparam>
    /// <returns>Typed representation of <paramref name="instance" /> into <typeparamref name="TValue" />.</returns>
    public static TValue GetTypedValue<TValue>(
        this string instance,
        string delimiter = ",",
        SerializerFormatEnum format = SerializerFormatEnum.None
    ) => (TValue)instance.GetTypedValue(typeof(TValue), delimiter, format);

    /// <summary>
    ///     This method attempts to convert the <see cref="string" /> <paramref name="instance" />
    ///     into the <paramref name="type" /> typed <paramref name="value" />.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> value to convert.</param>
    /// <param name="type">The expected resulting type.</param>
    /// <param name="value">The resulting output value.</param>
    /// <param name="delimiter">Optional, delimiter used for parsing.</param>
    /// <param name="format">Optional, serialization format used.</param>
    /// <returns>A <see cref="bool" /> indicating if the conversion was successful.</returns>
    public static bool TryGetTypedValue(
        this string instance,
        System.Type type,
        out dynamic value,
        string delimiter = ",",
        SerializerFormatEnum format = SerializerFormatEnum.None
    )
    {
        // Try to get the typed value.
        try
        {
            // Reset the typed value into the output.
            value = instance.GetTypedValue(type, delimiter, format);

            // We're done, return true as we converted the value.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the value as null into the output.
            value = type.GetDefaultValue();

            // We're done, return false as we couldn't convert the value.
            return false;
        }
    }

    /// <summary>
    ///     This method attempts to convert the <see cref="string" /> <paramref name="instance" />
    ///     into the <typeparamref name="TValue" /> typed <paramref name="value" />.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> value to convert.</param>
    /// <param name="value">The resulting output value.</param>
    /// <param name="delimiter">Optional, delimiter used for parsing.</param>
    /// <param name="format">Optional, serialization format used.</param>
    /// <typeparam name="TValue">The expected resulting type.</typeparam>
    /// <returns>A <see cref="bool" /> indicating if the conversion was successful.</returns>
    public static bool TryGetTypedValue<TValue>(
        this string instance,
        out TValue value,
        string delimiter = ",",
        SerializerFormatEnum format = SerializerFormatEnum.None
    )
    {
        // Try to get the typed value.
        if (instance.TryGetTypedValue(typeof(TValue), out dynamic result, delimiter, format))
        {
            // Reset the typed value into the output.
            value = (TValue)result;

            // We're done, return true as we converted the value.
            return true;
        }

        // Reset the value as null into the output.
        value = typeof(TValue).GetDefaultValue<TValue>();

        // We're done, return false as we couldn't convert the value.
        return false;
    }
}
