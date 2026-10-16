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
///     This static class converts a string into a value of a runtime <see cref="System.Type" />.
///     It is the inverse of <see cref="Object.ToStringValueObjectExtension" /> and is how the console
///     parser turns command-line text into typed property values.
///     <list type="bullet">
///         <item>
///             Quotes are stripped first. Blank, <c>nil</c> or <c>null</c> input gives the type's
///             default (<see langword="null" /> for nullable types).
///         </item>
///         <item>
///             With a <see cref="SerializerFormatEnum" /> other than <c>None</c>, the text is
///             deserialized in that format.
///         </item>
///         <item>
///             Scalars use their own parsers: enums by member name, description or number, booleans
///             as a whole word (true/false, yes/no, on/off, t/f, y/n, 1/0), bytes as hex, byte
///             arrays as Base64, dates, numbers, GUIDs, IP and email addresses.
///         </item>
///         <item>
///             Lists and arrays split on <c>delimiter</c>; dictionaries use <c>{k:v,...}</c>;
///             tuples use <c>(a,b,...)</c>. Each item is converted recursively.
///         </item>
///         <item>
///             Anything else is tried as JSON, then XML, then <see cref="Convert.ChangeType(object, System.Type)" />.
///         </item>
///     </list>
///     The file is named GetTypedValueStringExtension.cs; the class name drops the "d".
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
    ///     This method converts the <see cref="string" /> <paramref name="instance" /> to
    ///     <paramref name="type" /> using the rules in the class summary.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> value to convert.</param>
    /// <param name="type">The expected resulting type.</param>
    /// <param name="delimiter">Optional, delimiter used for parsing.</param>
    /// <param name="format">Optional, serialization format used.</param>
    /// <returns>Typed representation of <paramref name="instance" /> into <paramref name="type" />.</returns>
    /// <exception cref="Exception">
    ///     Any parse or format exception from the underlying converter when the text does not fit
    ///     the type.
    /// </exception>
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

        // Check for a string and return the sanitized instance.
        if (type == typeof(string))
            return instance;

        // Check for a serializer format then try to deserialize the value and return it.
        if (format is not SerializerFormatEnum.None)
            return SerializerService.TryDeserialize(type, instance, out object result, format)
                    ? result
                : Nullable.GetUnderlyingType(type) is null ? type.GetDefaultValue()
                : null;

        // Check for an enumerated value (or a nullable one) then match it by name, description
        // or number. Text that matches nothing is a format error, never the first member.
        if ((Nullable.GetUnderlyingType(type) ?? type).IsEnum)
            return instance.ToEnum(GetEnumDescriptionsTypeExtension.DefaultAttributeChecks, type)
                ?? throw new FormatException(
                    $"'{instance}' is not a value of {(Nullable.GetUnderlyingType(type) ?? type).Name}."
                );

        // Check for a boolean type then read the whole text as a boolean word. Anything else is a
        // format error, so TryGetTypedValue reports failure instead of guessing.
        if (type.IsBoolean())
            return IsStringExtension.TryReadBoolean(instance, out bool boolean)
                ? boolean
                : throw new FormatException($"'{instance}' is not a boolean word.");

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

        // Check for a byte type then parse it as hexadecimal.
        if (type == typeof(byte))
            return byte.Parse(instance, NumberStyles.HexNumber);

        // Check for a byte array type then decode it from Base64.
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

        // Check for an array or list type then split, convert each item and return the value.
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

            // Create a Dictionary<keyType, valueType> by reflection and fill it through Add.
            // LINQ's ToDictionary infers TKey/TValue from the selectors' return type, which is
            // object here, so it would produce a Dictionary<object, object>.
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

            // Ensure the syntax is correct for the tuple.
            if (!instance.StartsWith('(') || !instance.EndsWith(')'))
                throw new ArgumentException(
                    $"Invalid tuple syntax for type: {type.Name}. Expected format: (<item1>,<item2>,<item3>...).",
                    nameof(instance)
                );

            // Localize the element types for the tuple into this scope.
            System.Type[] elementTypes = type.GetTupleElementTypes();

            // Ensure the tuple has at least two element types.
            if (elementTypes.Length < 2)
                throw new ArgumentException(
                    $"Unable to determine element types for tuple: {type.Name}. Expected at least two element types.",
                    nameof(type)
                );

            // Ensure the number of elements matches the expected types. Split on `delimiter`, not a
            // hardcoded comma, so callers with a different separator can parse tuples.
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

            // Convert each element to its slot's type, then construct the tuple by reflection. Every
            // Tuple and ValueTuple arity has a public constructor taking its element values in order.
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
    ///     This method converts the <see cref="string" /> <paramref name="instance" /> to
    ///     <typeparamref name="TValue" />.
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
            // Output the type's default value.
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

        // Output the type's default value.
        value = typeof(TValue).GetDefaultValue<TValue>();

        // We're done, return false as we couldn't convert the value.
        return false;
    }
}
