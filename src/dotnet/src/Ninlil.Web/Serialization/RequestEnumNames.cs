using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Web.Serialization;

/// <summary>
///     This contract lets code that only holds a <see cref="System.Type" /> (the query and route
///     model binder) use the declared-name matching of <see cref="RequestEnumNames{TEnum}" />.
/// </summary>
internal interface IRequestEnumNames
{
    /// <summary>The names a 400 message lists, joined with commas.</summary>
    string ValidNames { get; }

    /// <summary>Parses declared-name text into the boxed enum value.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The boxed member, or null when the text is refused.</param>
    /// <returns><see langword="true" /> when the text is a declared name.</returns>
    bool TryParse(string text, out object value);
}

/// <summary>
///     This class holds the one definition of "a declared enum name" for API input. A declared
///     name is a member name or a name from <c>JsonStringEnumMemberName</c>, <c>EnumMember</c> or
///     <c>DisplayName</c>, or a name the type's own converter writes, matched without regard to
///     case and after trimming. <see cref="RequestEnumNameJsonConverter{TEnum}" /> (request
///     bodies) and <c>EnumNameModelBinder</c> (query and route values) both match through it, so
///     the two accept the same spellings and list the same valid names. The tables are built once
///     per enum type.
/// </summary>
/// <typeparam name="TEnum">The enum type.</typeparam>
internal sealed class RequestEnumNames<TEnum> : IRequestEnumNames
    where TEnum : struct, Enum
{
    private static readonly Lazy<Dictionary<string, TEnum>> Names = new(BuildNames);

    private static readonly Lazy<string> validNames = new(() =>
        string.Join(", ", WrittenPairs().Select(p => p.Name).Distinct(StringComparer.Ordinal))
    );

    /// <summary>The names the type's converter writes for each defined member.</summary>
    public static string ValidNames => validNames.Value;

    string IRequestEnumNames.ValidNames => ValidNames;

    bool IRequestEnumNames.TryParse(string text, out object value)
    {
        bool parsed = TryParse(text, out TEnum member);
        value = parsed ? member : null;
        return parsed;
    }

    private static IEnumerable<(TEnum Member, string Name)> WrittenPairs()
    {
        // The writer is built with default options, so the names do not depend on the host's
        // naming policy.
        JsonSerializerOptions options = new();
        var attributeConverter =
            (JsonConverter<TEnum>)AttributeConverters.Create(typeof(TEnum), options);
        foreach (TEnum value in Enum.GetValues<TEnum>())
        {
            using MemoryStream stream = new();
            using (Utf8JsonWriter jsonWriter = new(stream))
                attributeConverter.Write(jsonWriter, value, options);

            yield return (value, JsonSerializer.Deserialize<string>(stream.ToArray()));
        }
    }

    private static Dictionary<string, TEnum> BuildNames()
    {
        Dictionary<string, TEnum> map = new(StringComparer.OrdinalIgnoreCase);

        // Names come from the same three attributes the type's converter reads, plus the written
        // names, so every spelling the console takes from the OpenAPI document is accepted.
        foreach (
            FieldInfo field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static)
        )
        {
            var member = (TEnum)field.GetValue(null);
            map.TryAdd(field.Name, member);
            foreach (
                string description in typeof(TEnum).GetEnumDescriptions(field, DeclaredNameChecks)
            )
                map.TryAdd(description.Trim(), member);
        }

        foreach ((TEnum member, string name) in WrittenPairs())
            map.TryAdd(name, member);

        return map;
    }

    private static IEnumerable<GetEnumDescriptionsTypeExtension.EnumToStringExtensionAttributeDelegate> DeclaredNameChecks =>
        [
            attribute => attribute is JsonStringEnumMemberNameAttribute a ? a.Name.Trim() : null,
            attribute =>
                attribute is System.Runtime.Serialization.EnumMemberAttribute a
                && a.Value?.Trim() is not (null or { Length: 0 })
                    ? a.Value.Trim()
                    : null,
            attribute =>
                attribute is System.ComponentModel.DisplayNameAttribute a
                    ? a.DisplayName.Trim()
                    : null,
        ];

    /// <summary>
    ///     Parses declared-name text; a flags type takes a comma list of names.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="value">The member, or the default value when refused.</param>
    /// <returns><see langword="true" /> when every part is a declared name.</returns>
    public static bool TryParse(string text, out TEnum value)
    {
        value = default;
        if (text is null)
            return false;

        // A non-flags type takes exactly one name; a comma never belongs to a declared name.
        if (!typeof(TEnum).IsDefined(typeof(FlagsAttribute), false))
            return Names.Value.TryGetValue(text.Trim(), out value);

        // A flags list ORs its parts; every part must be a declared name, so "Sunday,3" and
        // "Sunday,,Monday" are refused.
        long combined = 0;
        foreach (string part in text.Split(','))
        {
            if (!Names.Value.TryGetValue(part.Trim(), out TEnum member))
                return false;

            combined |= Convert.ToInt64(member, System.Globalization.CultureInfo.InvariantCulture);
        }

        value = (TEnum)Enum.ToObject(typeof(TEnum), combined);
        return true;
    }
}
