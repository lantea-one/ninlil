using System.Reflection;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This class provides extensions for <see cref="string"/> values.
/// </summary>
public static class ToEnumStringExtension
{
    /// <summary>
    ///     This extension block provides methods to convert a <see cref="string"/> to a <see cref="System.Enum"/>.
    /// </summary>
    /// <param name="instance">The <see cref="string"/> value to convert.</param>
    extension(string instance)
    {
        /// <summary>
        ///     Converts a <see cref="string"/> to a <see cref="System.Enum"/>.
        /// </summary>
        /// <param name="type">The <see cref="System.Enum"/> type to convert.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>The <see cref="System.Enum"/> representation of the <c>instance</c> value.</returns>
        public System.Enum ToEnum(
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks,
            System.Type type = null
        )
        {
            // Check for a null or empty string then return null.
            if (instance?.Trim() is null or { Length: 0 })
                return null;

            // Localize our enum types.
            System.Type[] types = type is not null
                ? [type]
                :
                [
                    .. Assembly
                        .GetExecutingAssembly()
                        .GetTypes()
                        .Where(t => t.IsEnum && t.IsPublic),
                ];

            // Try to parse the enum value directly for the provided type then return if we've matched.
            try
            {
                // Localize the list of attributes to check for.
                var attributesChecksArray = attributeChecks?.ToArray() ?? [];

                // Iterate over the enum types, checking the enum value against the database value then returning if we have a match.
                foreach (System.Type t in types.Where(t => t.IsEnum))
                {
                    // Try to parse the enum value directly then return if we've matched.
                    if (System.Enum.TryParse(t, instance, true, out var result))
                        return result as System.Enum;

                    // Iterate through the fields of the enum type, checking the value against the database value then returning if we have a match.
                    foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                        if (
                            t.GetEnumDescriptions(field, attributesChecksArray)
                                .Any(d =>
                                    d
                                        ?.Trim()
                                        .Equals(instance.Trim(), StringComparison.OrdinalIgnoreCase)
                                        is true
                                )
                        )
                            return field.GetValue(null) as System.Enum;
                }
            }
            // Catch any exceptions that occur during parsing and return null since we can't find a match.
            catch (Exception)
            {
                return null;
            }

            // We're done, return default since we can't find a match.
            return type is not null && Nullable.GetUnderlyingType(type) is not null
                ? null
                : type?.GetDefaultValue();
        }

        /// <summary>
        ///     Converts a <see cref="string"/> to a <see cref="System.Enum"/>.
        /// </summary>
        /// <param name="type">The <see cref="System.Enum"/> type to convert.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>The <see cref="System.Enum"/> representation of the <c>instance</c> value.</returns>
        public System.Enum ToEnum(
            System.Type type = null,
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        ) => instance.ToEnum(attributeChecks.AsEnumerable(), type);

        /// <summary>
        ///     Converts a <see cref="string"/> to a <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type to convert.</typeparam>
        /// <returns>The <typeparamref name="TEnum" /> representation of the <c>instance</c> value.</returns>
        public TEnum ToEnum<TEnum>(
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks
        )
            where TEnum : System.Enum =>
            (TEnum)instance.ToEnum(attributeChecks.AsEnumerable(), typeof(TEnum));

        /// <summary>
        ///     Converts a <see cref="string"/> to a <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type to convert.</typeparam>
        /// <returns>The <typeparamref name="TEnum" /> representation of the <c>instance</c> value.</returns>
        public TEnum ToEnum<TEnum>(
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        )
            where TEnum : System.Enum => instance.ToEnum<TEnum>(attributeChecks.AsEnumerable());
    }

    /// <summary>
    ///     This extension block provides methods to try converting a
    ///     <see cref="string"/> to a <see cref="System.Enum"/> without
    ///     throwing exceptions.
    /// </summary>
    /// <param name="instance">The <see cref="string"/> value to convert.</param>
    extension(string instance)
    {
        /// <summary>
        ///     This extension method attempts to convert a <see cref="string"/> to a <see cref="System.Enum"/>.
        /// </summary>
        /// <param name="result">
        ///     The output parameter that will hold the converted
        ///     <see cref="System.Enum"/> value if the conversion
        ///     is successful.
        /// </param>
        /// <param name="attributeChecks">
        ///     The additional attributes to check for during the conversion.
        /// </param>
        /// <param name="type">
        ///     The <see cref="System.Enum"/> type to convert. If <see langword="null" />,
        ///     the method will attempt to find a matching enum type.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if the conversion is successful; otherwise, <see langword="false"/>.
        /// </returns>
        public bool TryToEnum(
            out System.Enum result,
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks,
            System.Type type = null
        )
        {
            // Try to convert the string to an enum value.
            var enumValue = instance.ToEnum(attributeChecks, type);

            // Check if the conversion was successful and assign the result accordingly.
            if (enumValue is not null)
            {
                // Reset the output model result to the converted enum value.
                result = enumValue;

                // We're done, return true since we successfully converted the string to an enum value.
                return true;
            }

            // Reset the output model result to null since we couldn't convert the string to an enum value.
            result = null;

            // We're done, return false since we couldn't convert the string to an enum value.
            return false;
        }

        /// <summary>
        ///     This extension method attempts to convert a <see cref="string"/> to a <see cref="System.Enum"/>.
        /// </summary>
        /// <param name="result">
        ///     The output parameter that will hold the converted
        ///     <see cref="System.Enum"/> value if the conversion
        ///     is successful.
        /// </param>
        /// <param name="type">
        ///     The <see cref="System.Enum"/> type to convert. If <see langword="null" />,
        ///     the method will attempt to find a matching enum type.
        /// </param>
        /// <param name="attributeChecks">
        ///     The additional attributes to check for during the conversion.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if the conversion is successful; otherwise, <see langword="false"/>.
        /// </returns>
        public bool TryToEnum(
            out System.Enum result,
            System.Type type = null,
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        ) => instance.TryToEnum(out result, attributeChecks.AsEnumerable(), type);
    }

    /// <summary>
    ///     This extension block provides methods to try converting a
    ///     <see cref="string"/> to a typed <see cref="System.Enum" /> values.
    ///     without throwing exceptions.
    /// </summary>
    /// <param name="instance">The <see cref="string"/> value to convert.</param>
    extension(string instance)
    {
        /// <summary>
        ///     This extension method attempts to convert a <see cref="string"/>
        ///     to a typed <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="result">
        ///     The output parameter that will hold the converted
        ///     <typeparamref name="TEnum" /> value if the conversion
        ///     is successful.
        /// </param>
        /// <param name="attributeChecks">
        ///     The additional attributes to check for during the conversion.
        /// </param>
        /// <typeparam name="TEnum">
        ///     The type of the enumeration to convert. Must be an enum type.
        /// </typeparam>
        /// <returns>
        ///     <see langword="true"/> if the conversion is successful; otherwise, <see langword="false"/>.
        /// </returns>
        public bool TryToEnum<TEnum>(
            out TEnum result,
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks
        )
            where TEnum : System.Enum
        {
            if (instance.TryToEnum(out var enumResult, attributeChecks, typeof(TEnum)))
            {
                // Reset the output model result to the converted enum value.
                result = (TEnum)enumResult;

                // We're done, return true since we successfully converted the string to an enum value.
                return true;
            }

            // Reset the output model result to the default value of TEnum
            // since we couldn't convert the string to an enum value.
            result = default;

            // We're done, return false since we couldn't convert the string to an enum value.
            return false;
        }

        /// <summary>
        ///     This extension method attempts to convert a <see cref="string"/>
        ///     to a typed <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="result">
        ///     The output parameter that will hold the converted
        ///     <typeparamref name="TEnum" /> value if the conversion
        ///     is successful.
        /// </param>
        /// <param name="attributeChecks">
        ///     The additional attributes to check for during the conversion.
        /// </param>
        /// <typeparam name="TEnum">
        ///     The type of the enumeration to convert. Must be an enum type.
        /// </typeparam>
        /// <returns>
        ///     <see langword="true"/> if the conversion is successful; otherwise, <see langword="false"/>.
        /// </returns>
        public bool TryToEnum<TEnum>(
            out TEnum result,
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        )
            where TEnum : System.Enum =>
            instance.TryToEnum(out result, attributeChecks.AsEnumerable());
    }
}
