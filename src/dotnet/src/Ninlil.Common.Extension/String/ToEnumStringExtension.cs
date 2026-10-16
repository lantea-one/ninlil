using System.Reflection;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This static class turns a string back into an enum value. It accepts a member name
///     (ignoring case), any description the caller's attribute checks extract from a member (see
///     <see cref="GetEnumDescriptionsTypeExtension" />), or a decimal number. It is the reverse of
///     <see cref="Ninlil.Common.Extension.Enum.ToDescriptionEnumExtension" />.
///     <para>
///         Text that names no member is refused: <c>ToEnum</c> returns <see langword="null" /> (no
///         fall back to the zero member), the <c>TryToEnum</c> methods return <see langword="false" />
///         and the generic <c>ToEnum</c> throws <see cref="ArgumentException" />. A silent zero member
///         would turn a typo in a request into a valid, wrong value.
///     </para>
///     <para>
///         A number is accepted when it fits the enum's underlying type, whether or not a member has
///         that value. Wire enums (DNS record types, classes, response codes) carry codes with no
///         member, and <see cref="Ninlil.Common.Extension.Enum.ToDescriptionEnumExtension" /> writes
///         such a code as its number, so the number must read back. A comma list is accepted only
///         for a <see cref="FlagsAttribute" /> enum.
///     </para>
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
        ///     Converts a <see cref="string"/> to a <see cref="System.Enum"/>, matching member names
        ///     (ignoring case) and member descriptions first, then a decimal number that fits the
        ///     underlying type, then (for a flags enum only) a comma list.
        /// </summary>
        /// <param name="type">
        ///     The <see cref="System.Enum"/> type to convert to. A <see cref="Nullable{T}" /> of an
        ///     enum is unwrapped. When null (kept optional for source compatibility), nothing can
        ///     match and the result is <see langword="null" />.
        /// </param>
        /// <param name="attributeChecks">The attribute checks that extract a description.</param>
        /// <returns>
        ///     The matching value, or <see langword="null" /> for blank input, a null or non-enum
        ///     <paramref name="type" />, or text that matches nothing.
        /// </returns>
        public System.Enum ToEnum(
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks,
            System.Type type = null
        )
        {
            // Check for blank input or a missing type then return null.
            if (instance?.Trim() is null or { Length: 0 } || type is null)
                return null;

            // Unwrap a nullable enum, then refuse anything that is not an enum.
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!type.IsEnum)
                return null;

            try
            {
                // Localize the trimmed text and the list of attributes to check for.
                string text = instance.Trim();
                var attributesChecksArray = attributeChecks?.ToArray() ?? [];

                // Match a member name or a description. An exact-case name wins over a name that
                // differs only by case.
                System.Enum caseInsensitive = null;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (field.Name.Equals(text, StringComparison.Ordinal))
                        return field.GetValue(null) as System.Enum;

                    if (
                        caseInsensitive is null
                        && (
                            field.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
                            || type.GetEnumDescriptions(field, attributesChecksArray)
                                .Any(d =>
                                    d?.Trim().Equals(text, StringComparison.OrdinalIgnoreCase)
                                        is true
                                )
                        )
                    )
                        caseInsensitive = field.GetValue(null) as System.Enum;
                }

                if (caseInsensitive is not null)
                    return caseInsensitive;

                // Accept a decimal number in the range of the underlying type, defined or not
                // (Enum.TryParse rejects a value that overflows the underlying type).
                if (IsDecimalNumber(text))
                    return System.Enum.TryParse(type, text, false, out var number)
                        ? number as System.Enum
                        : null;

                // Accept a comma list only for a flags enum; on any other enum it would silently
                // OR several members into a value no member has.
                if (text.Contains(',') && type.IsDefined(typeof(FlagsAttribute), false))
                    return System.Enum.TryParse(type, text, true, out var flags)
                        ? flags as System.Enum
                        : null;
            }
            // Catch any exceptions that occur during matching and return null since we can't find a match.
            catch (Exception)
            {
                return null;
            }

            // Nothing matched.
            return null;
        }

        /// <summary>
        ///     Converts a <see cref="string"/> to a <see cref="System.Enum"/>; see the overload taking an
        ///     <see cref="IEnumerable{T}" /> of checks for the rules.
        /// </summary>
        /// <param name="type">The <see cref="System.Enum"/> type to convert.</param>
        /// <param name="attributeChecks">The attribute checks that extract a description.</param>
        /// <returns>The matching value, or <see langword="null" /> when nothing matches.</returns>
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
        /// <exception cref="ArgumentException">
        ///     The <c>instance</c> is blank or matches no value of <typeparamref name="TEnum" />.
        /// </exception>
        public TEnum ToEnum<TEnum>(
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks
        )
            where TEnum : System.Enum =>
            instance.ToEnum(attributeChecks.AsEnumerable(), typeof(TEnum)) is TEnum value
                ? value
                : throw new ArgumentException(
                    $"'{instance}' is not a value of {typeof(TEnum).Name}."
                );

        /// <summary>
        ///     Converts a <see cref="string"/> to a <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type to convert.</typeparam>
        /// <returns>The <typeparamref name="TEnum" /> representation of the <c>instance</c> value.</returns>
        /// <exception cref="ArgumentException">
        ///     The <c>instance</c> is blank or matches no value of <typeparamref name="TEnum" />.
        /// </exception>
        public TEnum ToEnum<TEnum>(
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        )
            where TEnum : System.Enum => instance.ToEnum<TEnum>(attributeChecks.AsEnumerable());
    }

    /// <summary>
    ///     This extension block provides methods to try converting a
    ///     <see cref="string"/> to a <see cref="System.Enum"/> without
    ///     throwing exceptions. They return <see langword="false" /> only when
    ///     <c>ToEnum</c> returns <see langword="null" />.
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
        ///     nothing can match and the conversion fails.
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
        ///     nothing can match and the conversion fails.
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
    ///     <see cref="string"/> to a typed <see cref="System.Enum" /> value
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

    /// <summary>
    ///     Reports whether the text is an optional minus sign followed by decimal digits only.
    /// </summary>
    /// <param name="text">The trimmed text to check.</param>
    /// <returns><see langword="true" /> for a decimal integer.</returns>
    private static bool IsDecimalNumber(string text)
    {
        int start = text.StartsWith('-') ? 1 : 0;
        return text.Length > start && text.AsSpan(start).IndexOfAnyExceptInRange('0', '9') < 0;
    }
}
