using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This static class lists the descriptions (alternative names) of an enum type's members.
///     A description is whatever a caller-supplied attribute check extracts from an attribute on
///     the member, plus the member name itself. <see cref="Ninlil.Common.Extension.Enum.ToDescriptionEnumExtension" /> and
///     <see cref="Ninlil.Common.Extension.String.ToEnumStringExtension" /> use these lists to convert
///     in each direction.
/// </summary>
public static class GetEnumDescriptionsTypeExtension
{
    /// <summary>
    ///     This delegate extracts a description from one attribute on an enum member.
    /// </summary>
    /// <param name="attribute">The <see cref="Attribute"/> instance to check.</param>
    /// <returns>
    ///     The description to use for the <see cref="System.Enum"/> value
    ///     if the attribute matches; otherwise, <see langword="null" />.
    /// </returns>
    public delegate string EnumToStringExtensionAttributeDelegate(Attribute attribute);

    /// <summary>
    ///     This delegate names an attribute type and the property on it that holds the description.
    ///     The overloads that accept it read <c>attributeType</c> and <c>propertyName</c> from fields
    ///     of the delegate's target by reflection, so it only works when the delegate is a closure
    ///     that captures variables with those names; the delegate's parameters themselves are never
    ///     used.
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
    ///     This property returns the standard attribute checks, in order:
    ///     <see cref="EnumMemberAttribute" />, <see cref="JsonStringEnumMemberNameAttribute" />,
    ///     <see cref="XmlEnumAttribute" />, <see cref="YamlMemberAttribute" /> alias and
    ///     <see cref="DisplayNameAttribute" />. Nothing applies them automatically; pass them
    ///     explicitly.
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
        ///     This extension method lists every description of every member of <paramref name="type" />,
        ///     followed by the member names, without duplicates. A non-enum type gives an empty list.
        /// </summary>
        /// <param name="attributeChecks">The attribute checks that extract a description.</param>
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
        ///     This extension method lists the descriptions of one member, in attribute order, followed
        ///     by the member name.
        /// </summary>
        /// <param name="field">The field info for the enum member value.</param>
        /// <param name="attributeChecks">The attribute checks that extract a description.</param>
        /// <returns>The descriptions of the member, or an empty list for a null field or non-enum type.</returns>
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
                    // Add only this member's name as the fallback. Callers take FirstOrDefault(), so
                    // adding every member's name would make an unattributed member resolve to the
                    // first-declared member's name.
                    .Concat([field.Name])
                    .Where(d => d?.Trim() is not (null or ""))
                    .Distinct(StringComparer.Ordinal);

        /// <summary>
        ///     This extension method lists the descriptions of the member named
        ///     <paramref name="fieldName" />, followed by the member name.
        /// </summary>
        /// <param name="fieldName">The name of the enum member.</param>
        /// <param name="attributeChecks">The attribute checks that extract a description.</param>
        /// <returns>The descriptions of the member, or an empty list when there is no such member.</returns>
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
                    // Read the attribute type from a captured "attributeType" field on the
                    // delegate's closure (see the delegate's summary).
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

                    // Read the property name from a captured "propertyName" field on the closure.
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

                    // For an attribute of the named type, return the named property's value as a string;
                    // any other attribute gives null.
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
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
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
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
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
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
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
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
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
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
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
        /// <returns>A collection of descriptions for all values of the specified <see cref="System.Enum"/> type.</returns>
        public IEnumerable<string> GetEnumDescriptions<TEnum>(
            params EnumToStringExtensionAttributeMetaTypeDelegate[] attributeChecks
        )
            where TEnum : System.Enum =>
            typeof(TEnum).GetEnumDescriptions(attributeChecks.AsEnumerable());
    }
}
