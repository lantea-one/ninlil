using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Enum;

/// <summary>
///     This class provides extensions for <see cref="System.Enum" /> values.
/// </summary>
public static class ToDescriptionEnumExtension
{
    /// <param name="enumInstance">The <see cref="System.Enum" /> value to convert.</param>
    extension(System.Enum enumInstance)
    {
        /// <summary>
        ///     Converts a <see cref="System.Enum" /> <c>enumInstance</c> into a <see cref="string"/>.
        /// </summary>
        /// <param name="type">The <see cref="System.Enum" /> type to convert.</param>
        /// <param name="value">The specific value to convert in direct form or <see cref="string" /> form.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>The <see cref="string" /> representation of the <c>enumInstance</c>.</returns>
        public string ToDescription(
            System.Type type,
            object value,
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks
        )
        {
            // Check for a null instance, then return null.
            if (enumInstance is null)
                return null;

            // Ensure we have a string value for the enum.
            string enumValueName = value as string ?? value?.ToString()?.Trim();

            // Check for any members that match the provided value, then return any description matches for it.
            if (type.GetField(enumValueName?.Trim() ?? "") is not null)
                return type.GetEnumDescriptions(enumValueName, attributeChecks).FirstOrDefault()
                    ?? enumInstance.ToString();

            // Localize the enum value descriptions for the provided type and additional attributes.
            string[] descriptions = [.. type.GetEnumDescriptions(attributeChecks)];

            // We're done, check for a match on the enum value name then return the description if we've matched.
            // Otherwise, return the instance as a string.
            return descriptions.Any(d =>
                d.Trim().Equals(enumValueName?.Trim(), StringComparison.OrdinalIgnoreCase)
            )
                ? descriptions.First(d =>
                    d.Trim().Equals(enumValueName?.Trim(), StringComparison.OrdinalIgnoreCase)
                )
                : enumInstance.ToString();
        }

        /// <summary>
        ///     Converts a <see cref="System.Enum" /> <c>enumInstance</c> into a <see cref="string"/>.
        /// </summary>
        /// <param name="type">The <see cref="System.Enum" /> type to convert.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>The <see cref="string" /> representation of the <c>enumInstance</c>.</returns>
        public string ToDescription(
            System.Type type,
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        ) =>
            enumInstance.ToDescription(
                type,
                enumInstance.ToString(),
                attributeChecks.AsEnumerable()
            );
    }

    /// <param name="enumInstance">The <typeparamref name="TEnum" /> value to convert.</param>
    /// <typeparam name="TEnum">The <see cref="System.Enum"/> type to convert.</typeparam>
    extension<TEnum>(TEnum enumInstance)
        where TEnum : System.Enum
    {
        /// <summary>
        ///     Converts a <typeparamref name="TEnum" /> <c>enumInstance</c> into a <see cref="string"/>.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>The <see cref="string" /> representation of the <c>enumInstance</c>.</returns>
        public string ToDescription(
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks
        ) => enumInstance.ToDescription(typeof(TEnum), enumInstance.ToString(), attributeChecks);

        /// <summary>
        ///     Converts a <typeparamref name="TEnum" /> <c>enumInstance</c> into a <see cref="string"/>.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>The <see cref="string" /> representation of the <c>enumInstance</c>.</returns>
        public string ToDescription(
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        ) => enumInstance.ToDescription(typeof(TEnum), enumInstance.ToString(), attributeChecks);

        /// <summary>
        ///     Converts a <typeparamref name="TEnum" /> <c>enumInstance</c> into a <see cref="string"/>.
        /// </summary>
        /// <param name="value">The specific value to convert in direct form or <see cref="string" /> form.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>The <see cref="string" /> representation of the <c>enumInstance</c> value.</returns>
        public string ToDescription(
            object value,
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        ) => enumInstance.ToDescription(typeof(TEnum), value.ToString(), attributeChecks);
    }

    /// <param name="enumInstance">The <see cref="System.Enum" /> value to convert.</param>
    extension(System.Enum enumInstance)
    {
        /// <summary>
        ///     This method tries to convert a <see cref="System.Enum" />
        ///     <c>enumInstance</c> into a <see cref="string" />.
        /// </summary>
        /// <param name="enumeratedType">The <see cref="System.Enum" /> type to convert.</param>
        /// <param name="description">
        ///     The output <see cref="string" /> representation of the <c>enumInstance</c> value.
        /// </param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A <see cref="bool" /> denoting whether the conversion was successful or not.</returns>
        public bool TryToDescription(
            System.Type enumeratedType,
            out string description,
            GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        )
        {
            // Ensure we have a description set.
            description = null;

            // Check for a null instance, then return false.
            if (enumInstance is null)
                return false;

            // Convert the instance to a string value.
            description = enumInstance.ToDescription(enumeratedType, attributeChecks);

            // We're done, check for a null or empty description, then return false.  Otherwise, return true.
            return description?.Trim() is not (null or { Length: 0 });
        }

        /// <summary>
        ///     This method tries to convert a <see cref="System.Enum" />
        ///     <c>enumInstance</c> into a <see cref="string" />.
        /// </summary>
        /// <param name="enumeratedType">The <see cref="System.Enum" /> type to convert.</param>
        /// <param name="value">The specific value to convert in direct form or <see cref="string" /> form.</param>
        /// <param name="description">
        ///     The output <see cref="string" /> representation of the <c>enumInstance</c> value.
        /// </param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A <see cref="bool" /> denoting whether the conversion was successful or not.</returns>
        public bool TryToDescription(
            System.Type enumeratedType,
            object value,
            out string description,
            GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        )
        {
            // Ensure we have a description set.
            description = null;

            // Check for a null instance, then return false.
            if (enumInstance is null)
                return false;

            // Convert the instance to a string value.
            description = enumInstance.ToDescription(enumeratedType, value, attributeChecks);

            // We're done, check for a null or empty description, then return false.  Otherwise, return true.
            return description?.Trim() is not (null or { Length: 0 });
        }
    }

    /// <param name="instance">The <typeparamref name="TEnum" /> value to convert.</param>
    /// <typeparam name="TEnum">The <see cref="System.Enum"/> type to convert.</typeparam>
    extension<TEnum>(TEnum instance)
        where TEnum : System.Enum
    {
        /// <summary>
        ///     This method tries to convert a <typeparamref name="TEnum" />
        ///     <c>instance</c> value into a <see cref="string" />.
        /// </summary>
        /// <param name="description">
        ///     The output <see cref="string" /> representation of the <c>instance</c> value.
        /// </param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>
        ///     A <see cref="bool" /> denoting whether the conversion was successful or not.
        /// </returns>
        public bool TryToDescription(
            out string description,
            IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> attributeChecks
        ) =>
            instance.TryToDescription(
                typeof(TEnum),
                instance,
                out description,
                [.. attributeChecks]
            );

        /// <summary>
        ///     This method tries to convert a <typeparamref name="TEnum" />
        ///     <c>instance</c> value into a <see cref="string" />.
        /// </summary>
        /// <param name="description">
        ///     The output <see cref="string" /> representation of the <c>instance</c> value.
        /// </param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A <see cref="bool" /> denoting whether the conversion was successful or not.</returns>
        public bool TryToDescription(
            out string description,
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        ) => instance.TryToDescription(out description, attributeChecks.AsEnumerable());

        /// <summary>
        ///     This method tries to convert a <typeparamref name="TEnum" />
        ///     <c>instance</c> value into a <see cref="string" />.
        /// </summary>
        /// <param name="value">The specific value to convert in direct form or <see cref="string" /> form.</param>
        /// <param name="description">
        ///     The output <see cref="string" /> representation of the <c>instance</c> value.
        /// </param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A <see cref="bool" /> denoting whether the conversion was successful or not.</returns>
        public bool TryToDescription(
            object value,
            out string description,
            params GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate[] attributeChecks
        ) => instance.TryToDescription(typeof(TEnum), value, out description, attributeChecks);
    }
}
