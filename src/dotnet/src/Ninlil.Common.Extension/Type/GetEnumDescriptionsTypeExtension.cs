using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This extension class provides methods for retrieving descriptions from <see cref="System.Enum"/> types.
/// </summary>
public static class GetEnumDescriptionsTypeExtension
{
    /// <summary>
    ///     This delegate defines the structure for adding additional attributes
    ///     to check for descriptions on <see cref="System.Enum"/> values.
    /// </summary>
    /// <param name="attribute">The <see cref="Attribute"/> instance to check.</param>
    /// <returns>
    ///     The description to use for the <see cref="System.Enum"/> value
    ///     if the attribute matches; otherwise, <see langword="null" />.
    /// </returns>
    public delegate string EnumToStringExtensionAttributeDelegate(Attribute attribute);

    /// <summary>
    ///     This delegate defines the structure for adding additional attributes
    ///     to check for descriptions on <see cref="System.Enum"/> values, with access
    ///     to the <paramref name="attributeType" /> and <paramref name="propertyName" />
    ///     for dynamic checks using reflection.
    /// </summary>
    /// <param name="attributeType">The type of the attribute to check.</param>
    /// <param name="propertyName">
    ///     The name of the property on the <paramref name="attributeType" /> that contains the description value.
    /// </param>
    /// <returns>
    ///     The description to use for the <see cref="System.Enum"/> value
    ///     if the attribute matches; otherwise, <see langword="null" />.
    /// </returns>
    public delegate string EnumToStringExtensionAttributeMetaTypeDelegate(
        System.Type attributeType,
        string propertyName
    );

    /// <summary>
    ///     This field defines the default checks for checking the various
    ///     display name attributes for an <see cref="System.Enum" /> value.
    /// </summary>
    public static IEnumerable<EnumToStringExtensionAttributeDelegate> DefaultAttributeChecks =>
        [
            attribute =>
                attribute is EnumMemberAttribute a && a.Value?.Trim() is not (null or { Length: 0 })
                    ? a.Value.Trim()
                    : null,
            attribute => attribute is JsonStringEnumMemberNameAttribute a ? a.Name.Trim() : null,
            attribute => attribute is XmlEnumAttribute a ? a.Name?.Trim() : null,
            attribute => attribute is YamlMemberAttribute a ? a.Alias?.Trim() : null,
            attribute => attribute is DisplayNameAttribute a ? a.DisplayName.Trim() : null,
        ];

    /// <summary>
    ///     This extension block provides methods for retrieving descriptions from <see cref="System.Enum"/> types.
    /// </summary>
    /// <param name="type">
    ///     The <see cref="System.Type"/> representing the <see cref="System.Enum"/>
    ///     type for which to retrieve descriptions.
    /// </param>
    extension(System.Type type)
    {
        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <paramref name="type"/>.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions(
            IEnumerable<EnumToStringExtensionAttributeDelegate> attributeChecks
        ) =>
            !type.IsEnum
                ? []
                : type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .SelectMany(f => type.GetEnumDescriptions(f, attributeChecks))
                    .Concat(type.GetEnumNames())
                    .Concat(type.GetEnumValues().Cast<System.Enum>().Select(e => e.ToString()))
                    .Where(d => d?.Trim() is not (null or ""))
                    .Distinct(StringComparer.Ordinal);

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <paramref name="type"/>.
        /// </summary>
        /// <param name="field">The field info for the enum member value.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions(
            FieldInfo field,
            IEnumerable<EnumToStringExtensionAttributeDelegate> attributeChecks
        ) =>
            !type.IsEnum || field is null
                ? []
                : type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f == field)
                    .SelectMany(f => f.GetCustomAttributes())
                    .SelectMany(a => (attributeChecks ?? []).Select(c => c.Invoke(a)))
                    // Scope the fallback name to this one field only -- concatenating every
                    // member's name/value here (as an earlier version of this method did) meant an
                    // unattributed member other than the type's first-declared one would resolve to
                    // that first member's name instead of its own, since FirstOrDefault() at the
                    // call site always picked the earliest match in declaration order.
                    .Concat([field.Name])
                    .Where(d => d?.Trim() is not (null or ""))
                    .Distinct(StringComparer.Ordinal);

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <paramref name="type"/>.
        /// </summary>
        /// <param name="fieldName">The name of the field on the enum type to check for additional attributes.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions(
            string fieldName,
            IEnumerable<EnumToStringExtensionAttributeDelegate> attributeChecks
        ) =>
            !type.IsEnum
                ? []
                : type.GetEnumDescriptions(
                    type.GetField(fieldName.Trim(), BindingFlags.Public | BindingFlags.Static),
                    attributeChecks
                );

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <paramref name="type"/>.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions(
            params EnumToStringExtensionAttributeDelegate[] attributeChecks
        ) => type.GetEnumDescriptions(attributeChecks.AsEnumerable());

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <paramref name="type"/>.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions(
            IEnumerable<EnumToStringExtensionAttributeMetaTypeDelegate> attributeChecks
        ) =>
            type.GetEnumDescriptions(
                attributeChecks?.Select(d => new EnumToStringExtensionAttributeDelegate(attribute =>
                {
                    // Localize the value of the first parameter on the incoming
                    // delegate, which should be the attribute type we're checking for.
                    System.Type attributeType = d
                        .Target?.GetType()
                        .GetFields()
                        .Where(f =>
                            f.Name.Equals("attributeType", StringComparison.OrdinalIgnoreCase)
                        )
                        .Select(f => f.GetValue(d.Target) as System.Type)
                        .FirstOrDefault();

                    // Ensure we have a valid attribute type to check against; otherwise, return null.
                    if (attributeType is null)
                        return null;

                    // Localize the property name value of the second parameter on the incoming delegate,
                    // which should be the name of the property on the attribute that contains the
                    // description value.
                    string propertyName = d
                        .Target?.GetType()
                        .GetFields()
                        .Where(f =>
                            f.Name.Equals("propertyName", StringComparison.OrdinalIgnoreCase)
                        )
                        .Select(f => f.GetValue(d.Target)?.ToString())
                        .FirstOrDefault();

                    // Ensure we have a valid property name to check against; otherwise, return null.
                    if (propertyName?.Trim() is null or { Length: 0 })
                        return null;

                    // Ensure the attribute we're checking is of the correct type; otherwise, return null. Otherwise,
                    // use reflection to get the value of the specified property on the attribute, which should contain
                    // the description value, and return it if it's a valid string; otherwise, return null.
                    return attribute.GetType() != attributeType
                        ? null
                        : attribute
                            .GetType()
                            .GetProperty(propertyName)
                            ?.GetValue(attribute)
                            ?.ToString();
                }))
            );

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <paramref name="type"/>.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions(
            params EnumToStringExtensionAttributeMetaTypeDelegate[] attributeChecks
        ) => type.GetEnumDescriptions(attributeChecks?.AsEnumerable());

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type for which to retrieve descriptions.</typeparam>
        /// <returns>>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions<TEnum>(
            IEnumerable<EnumToStringExtensionAttributeDelegate> attributeChecks
        )
            where TEnum : System.Enum => typeof(TEnum).GetEnumDescriptions(attributeChecks);

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="field">The field info for the enum member value.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type for which to retrieve descriptions.</typeparam>
        /// <returns>>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions<TEnum>(
            FieldInfo field,
            IEnumerable<EnumToStringExtensionAttributeDelegate> attributeChecks
        )
            where TEnum : System.Enum => typeof(TEnum).GetEnumDescriptions(field, attributeChecks);

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="fieldName">The name of the field on the enum type to check for additional attributes.</param>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type for which to retrieve descriptions.</typeparam>
        /// <returns>>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions<TEnum>(
            string fieldName,
            IEnumerable<EnumToStringExtensionAttributeDelegate> attributeChecks
        )
            where TEnum : System.Enum =>
            typeof(TEnum).GetEnumDescriptions(fieldName, attributeChecks);

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type for which to retrieve descriptions.</typeparam>
        /// <returns>>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions<TEnum>(
            params EnumToStringExtensionAttributeDelegate[] attributeChecks
        )
            where TEnum : System.Enum =>
            typeof(TEnum).GetEnumDescriptions(attributeChecks.AsEnumerable());

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type for which to retrieve descriptions.</typeparam>
        /// <returns>>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions<TEnum>(
            IEnumerable<EnumToStringExtensionAttributeMetaTypeDelegate> attributeChecks
        )
            where TEnum : System.Enum => typeof(TEnum).GetEnumDescriptions(attributeChecks);

        /// <summary>
        ///     This extension method retrieves the descriptions for all values
        ///     of an <see cref="System.Enum"/> <typeparamref name="TEnum" />.
        /// </summary>
        /// <param name="attributeChecks">The additional attributes to check for.</param>
        /// <typeparam name="TEnum">The <see cref="System.Enum"/> type for which to retrieve descriptions.</typeparam>
        /// <returns>>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions<TEnum>(
            params EnumToStringExtensionAttributeMetaTypeDelegate[] attributeChecks
        )
            where TEnum : System.Enum =>
            typeof(TEnum).GetEnumDescriptions(attributeChecks.AsEnumerable());
    }
}
