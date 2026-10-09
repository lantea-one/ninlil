using System.Globalization;
using System.Text.Json;
using Ninlil.Dns.Common.Exceptions;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="JsonElement" /> extension for reading one field of an
///     RFC 7396 JSON Merge Patch body, the format of every <c>PATCH</c> in the REST API. An
///     absent field is left as it is, a field set to JSON <c>null</c> is cleared, and any other
///     value replaces the field. Each controller's <c>PATCH</c> action applies these rules field
///     by field rather than through a generic reflection-based merger, which stays readable for
///     the number of models involved.
///     <para>
///         The typed readers (<c>TryGetMergePatch*</c>) replace the raw
///         <see cref="JsonElement.GetInt32" /> family in those actions. Each returns
///         <see langword="false" /> for an absent field and <see langword="true" /> with the
///         value for a present one. A present field of the wrong JSON kind, or outside the range
///         the caller states, throws <see cref="ValidationException" /> (HTTP 400) with a message
///         that names the field, where the raw reads would throw
///         <see cref="InvalidOperationException" /> and surface as HTTP 500. There is no default
///         range: every numeric and duration call site states its own, so none can be forgotten.
///         Actions that are not converted yet are covered by
///         <see cref="ExceptionHandling.MergePatchBodyFilter" />.
///     </para>
/// </summary>
public static class JsonElementMergePatchExtension
{
    /// <summary>
    ///     This method looks up a single field in a merge patch document.
    /// </summary>
    /// <param name="instance">The patch document to look up the field in.</param>
    /// <param name="propertyName">The field's JSON property name (camelCase).</param>
    /// <param name="value">The field's value, if present.</param>
    /// <returns>
    ///     Whether the field is present; always <see langword="false" /> when the document is not
    ///     a JSON object. The caller must still check <paramref name="value" />'s
    ///     <see cref="JsonElement.ValueKind" /> for <see cref="JsonValueKind.Null" /> to tell
    ///     "clear this field" from a replacement value.
    /// </returns>
    public static bool TryGetMergePatchField(
        this JsonElement instance,
        string propertyName,
        out JsonElement value
    )
    {
        value = default;
        return instance.ValueKind == JsonValueKind.Object
            && instance.TryGetProperty(propertyName, out value);
    }

    /// <summary>
    ///     This constant is the longest duration that <c>Task.Delay</c>,
    ///     <c>CancellationTokenSource.CancelAfter</c> and <c>PeriodicTimer</c> accept
    ///     (<c>uint.MaxValue - 1</c> milliseconds, about 49.7 days). A duration that feeds one of
    ///     them must use a maximum no larger than this.
    /// </summary>
    public static readonly TimeSpan TimerMaximum = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    ///     This constant is the longest duration that <c>HttpClient.Timeout</c> accepts
    ///     (<c>int.MaxValue</c> milliseconds, about 24.8 days).
    /// </summary>
    public static readonly TimeSpan HttpTimeoutMaximum = TimeSpan.FromMilliseconds(int.MaxValue);

    /// <summary>
    ///     This method reads a boolean field: only JSON <c>true</c> and <c>false</c> are accepted.
    /// </summary>
    /// <param name="instance">The patch document.</param>
    /// <param name="name">The field's JSON property name (camelCase), looked up in the document.</param>
    /// <param name="value">The field's value, if present.</param>
    /// <param name="fieldPath">
    ///     The name to put in error messages when it differs from <paramref name="name" />, for
    ///     example <c>certificate.enable</c> for a field of a nested object. Defaults to the name.
    /// </param>
    /// <returns>Whether the field is present.</returns>
    /// <exception cref="ValidationException">The field is present but not a boolean.</exception>
    public static bool TryGetMergePatchBoolean(
        this JsonElement instance,
        string name,
        out bool value,
        string fieldPath = null
    )
    {
        value = false;
        if (!instance.TryGetMergePatchField(name, out JsonElement element))
            return false;

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ValidationException($"'{fieldPath ?? name}' must be true or false.");

        value = element.GetBoolean();
        return true;
    }

    /// <summary>
    ///     This method reads a whole-number field and checks it against an inclusive range. A
    ///     fraction (<c>1.5</c>), a number outside the <see cref="int" /> range, a string and
    ///     <c>null</c> are all rejected.
    /// </summary>
    /// <param name="instance">The patch document.</param>
    /// <param name="name">The field's JSON property name (camelCase), looked up in the document.</param>
    /// <param name="value">The field's value, if present.</param>
    /// <param name="minimum">The smallest accepted value.</param>
    /// <param name="maximum">The largest accepted value.</param>
    /// <param name="fieldPath">The name to use in error messages (see <see cref="TryGetMergePatchBoolean" />).</param>
    /// <returns>Whether the field is present.</returns>
    /// <exception cref="ValidationException">The field is present but invalid.</exception>
    public static bool TryGetMergePatchInt32(
        this JsonElement instance,
        string name,
        out int value,
        int minimum,
        int maximum,
        string fieldPath = null
    )
    {
        value = 0;
        if (!instance.TryGetMergePatchField(name, out JsonElement element))
            return false;

        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt32(out int parsed)
            || parsed < minimum
            || parsed > maximum
        )
        {
            throw new ValidationException(
                $"'{fieldPath ?? name}' must be a whole number between {minimum} and {maximum}."
            );
        }

        value = parsed;
        return true;
    }

    /// <summary>
    ///     This method reads a number field and checks it against an inclusive range. Only finite
    ///     numbers are accepted.
    /// </summary>
    /// <param name="instance">The patch document.</param>
    /// <param name="name">The field's JSON property name (camelCase), looked up in the document.</param>
    /// <param name="value">The field's value, if present.</param>
    /// <param name="minimum">The smallest accepted value.</param>
    /// <param name="maximum">The largest accepted value.</param>
    /// <param name="fieldPath">The name to use in error messages (see <see cref="TryGetMergePatchBoolean" />).</param>
    /// <returns>Whether the field is present.</returns>
    /// <exception cref="ValidationException">The field is present but invalid.</exception>
    public static bool TryGetMergePatchDouble(
        this JsonElement instance,
        string name,
        out double value,
        double minimum,
        double maximum,
        string fieldPath = null
    )
    {
        value = 0;
        if (!instance.TryGetMergePatchField(name, out JsonElement element))
            return false;

        if (
            element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out double parsed)
            || !double.IsFinite(parsed)
            || parsed < minimum
            || parsed > maximum
        )
        {
            throw new ValidationException(
                $"'{fieldPath ?? name}' must be a number between "
                    + $"{minimum.ToString(CultureInfo.InvariantCulture)} and "
                    + $"{maximum.ToString(CultureInfo.InvariantCulture)}."
            );
        }

        value = parsed;
        return true;
    }

    /// <summary>
    ///     This method reads a string field. JSON <c>null</c> is accepted only when
    ///     <paramref name="allowNull" /> is set, and then yields <see langword="null" />, which
    ///     means "clear this field" in a merge patch.
    /// </summary>
    /// <param name="instance">The patch document.</param>
    /// <param name="name">The field's JSON property name (camelCase), looked up in the document.</param>
    /// <param name="value">The field's value, if present; <see langword="null" /> for a cleared field.</param>
    /// <param name="allowNull">Whether JSON <c>null</c> is a valid value for this field.</param>
    /// <param name="fieldPath">The name to use in error messages (see <see cref="TryGetMergePatchBoolean" />).</param>
    /// <returns>Whether the field is present.</returns>
    /// <exception cref="ValidationException">The field is present but not a string.</exception>
    public static bool TryGetMergePatchString(
        this JsonElement instance,
        string name,
        out string value,
        bool allowNull,
        string fieldPath = null
    )
    {
        value = null;
        if (!instance.TryGetMergePatchField(name, out JsonElement element))
            return false;

        if (element.ValueKind == JsonValueKind.Null && allowNull)
            return true;

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ValidationException(
                $"'{fieldPath ?? name}' must be a string{(allowNull ? " or null" : string.Empty)}."
            );
        }

        value = element.GetString();
        return true;
    }

    /// <summary>
    ///     This method reads a duration field written as a <see cref="TimeSpan" /> string
    ///     (<c>[d.]hh:mm:ss</c>, for example <c>00:00:30</c> or <c>1.12:00:00</c>) and checks it
    ///     against an inclusive range. Parsing uses the invariant culture, so the result does not
    ///     depend on the server's locale.
    /// </summary>
    /// <param name="instance">The patch document.</param>
    /// <param name="name">The field's JSON property name (camelCase), looked up in the document.</param>
    /// <param name="value">The field's value, if present.</param>
    /// <param name="minimum">The shortest accepted duration.</param>
    /// <param name="maximum">
    ///     The longest accepted duration. Use <see cref="TimerMaximum" /> or
    ///     <see cref="HttpTimeoutMaximum" /> (or less) when the value reaches a timer or an
    ///     <c>HttpClient</c>.
    /// </param>
    /// <param name="fieldPath">The name to use in error messages (see <see cref="TryGetMergePatchBoolean" />).</param>
    /// <returns>Whether the field is present.</returns>
    /// <exception cref="ValidationException">The field is present but invalid.</exception>
    public static bool TryGetMergePatchDuration(
        this JsonElement instance,
        string name,
        out TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string fieldPath = null
    )
    {
        value = TimeSpan.Zero;
        if (!instance.TryGetMergePatchField(name, out JsonElement element))
            return false;

        if (
            element.ValueKind != JsonValueKind.String
            || !TimeSpan.TryParse(
                element.GetString(),
                CultureInfo.InvariantCulture,
                out TimeSpan parsed
            )
            || parsed < minimum
            || parsed > maximum
        )
        {
            throw new ValidationException(
                $"'{fieldPath ?? name}' must be a duration in [d.]hh:mm:ss form, such as "
                    + "'00:00:30', between "
                    + $"'{minimum.ToString("c", CultureInfo.InvariantCulture)}' and "
                    + $"'{maximum.ToString("c", CultureInfo.InvariantCulture)}'."
            );
        }

        value = parsed;
        return true;
    }

    /// <summary>
    ///     This method reads a field that is a JSON array of strings. JSON <c>null</c> is
    ///     accepted only when <paramref name="allowNull" /> is set, and then yields
    ///     <see langword="null" /> ("clear this field"). The caller still validates each element
    ///     (IP address, CIDR, DNS name).
    /// </summary>
    /// <param name="instance">The patch document.</param>
    /// <param name="name">The field's JSON property name (camelCase), looked up in the document.</param>
    /// <param name="value">The field's elements, if present; <see langword="null" /> for a cleared field.</param>
    /// <param name="allowNull">Whether JSON <c>null</c> is a valid value for this field.</param>
    /// <param name="fieldPath">The name to use in error messages (see <see cref="TryGetMergePatchBoolean" />).</param>
    /// <returns>Whether the field is present.</returns>
    /// <exception cref="ValidationException">
    ///     The field is present but not an array, or an element is not a string.
    /// </exception>
    public static bool TryGetMergePatchStringList(
        this JsonElement instance,
        string name,
        out List<string> value,
        bool allowNull,
        string fieldPath = null
    )
    {
        value = null;
        if (!instance.TryGetMergePatchField(name, out JsonElement element))
            return false;

        if (element.ValueKind == JsonValueKind.Null && allowNull)
            return true;

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ValidationException(
                $"'{fieldPath ?? name}' must be an array of strings"
                    + $"{(allowNull ? " or null" : string.Empty)}."
            );
        }

        List<string> items = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ValidationException(
                    $"'{fieldPath ?? name}' must be an array of strings."
                );

            items.Add(item.GetString());
        }

        value = items;
        return true;
    }

    /// <summary>
    ///     This method reads an enumeration field written as the member's name, matched without
    ///     regard to case. Numbers (including numeric strings) and undefined names are rejected,
    ///     so a value outside the enumeration never reaches the configuration file.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration type.</typeparam>
    /// <param name="instance">The patch document.</param>
    /// <param name="name">The field's JSON property name (camelCase), looked up in the document.</param>
    /// <param name="value">The field's value, if present.</param>
    /// <param name="fieldPath">The name to use in error messages (see <see cref="TryGetMergePatchBoolean" />).</param>
    /// <returns>Whether the field is present.</returns>
    /// <exception cref="ValidationException">The field is present but invalid.</exception>
    public static bool TryGetMergePatchEnum<TEnum>(
        this JsonElement instance,
        string name,
        out TEnum value,
        string fieldPath = null
    )
        where TEnum : struct, Enum
    {
        value = default;
        if (!instance.TryGetMergePatchField(name, out JsonElement element))
            return false;

        string text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        foreach (string memberName in Enum.GetNames<TEnum>())
        {
            if (string.Equals(memberName, text, StringComparison.OrdinalIgnoreCase))
            {
                value = Enum.Parse<TEnum>(memberName);
                return true;
            }
        }

        throw new ValidationException(
            $"'{fieldPath ?? name}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}."
        );
    }
}
