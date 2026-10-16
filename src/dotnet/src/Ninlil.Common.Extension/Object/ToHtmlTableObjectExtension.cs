using System.Reflection;
using System.Text;
using MimeKit.Text;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Object;

/// <summary>
///     This static class renders an object, a <c>Dictionary&lt;string, object&gt;</c> or a list of
///     such dictionaries as an HTML table using MimeKit's <see cref="HtmlWriter" />. Public
///     properties (or dictionary keys) become the header cells and each object becomes one body row
///     with one cell per header, in header order. A list of dictionaries takes its headers from the
///     union of all keys in first-seen order and leaves the cell empty where a dictionary has no
///     such key. A cell value that is a dictionary, a list of dictionaries or a nested class is
///     written once, as a nested table; anything else is written as escaped text. A nested
///     reference that is already being rendered (a cycle) is written as its type name.
/// </summary>
public static class ToHtmlTableObjectExtension
{
    /// <summary>
    ///     This method renders a dictionary as an HTML table: the keys as header cells, then one row
    ///     with one cell per key.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="Dictionary{TKey,TValue}" /> to convert where
    ///     the key is a <see cref="string" /> and the value is an <see cref="object" />.
    /// </param>
    /// <param name="tableAttributes">Optional attributes for the HTML table element.</param>
    /// <param name="rowOnly">
    ///     Optional flag to indicate whether to write only the row of
    ///     data for the table without the header or surrounding elements.
    /// </param>
    /// <param name="rendering">The references being rendered now, for the cycle rule.</param>
    /// <param name="stoppingToken">Optional cancellation token to stop the operation.</param>
    /// <returns>A string representation of the <paramref name="instance" /> as an HTML table.</returns>
    private static Task<string> ToHtmlTableAsync(
        Dictionary<string, object> instance,
        Dictionary<string, object> tableAttributes,
        bool rowOnly,
        HashSet<object> rendering,
        CancellationToken stoppingToken
    ) => WriteTableAsync(instance, [instance], tableAttributes, rowOnly, rendering, stoppingToken);

    /// <summary>
    ///     This method renders a list of dictionaries as one HTML table, taking the header cells from
    ///     the union of the dictionaries' keys and writing one row per dictionary.
    /// </summary>
    /// <param name="instance">The instance of the <see cref="IEnumerable{Dictionary}"/> to convert.</param>
    /// <param name="tableAttributes">Optional attributes for the HTML table element.</param>
    /// <param name="rowsOnly">
    ///     Optional flag to indicate whether to write only the rows of
    ///     data for the table without the header or surrounding elements.
    /// </param>
    /// <param name="rendering">The references being rendered now, for the cycle rule.</param>
    /// <param name="stoppingToken">Optional cancellation token to stop the operation.</param>
    /// <returns>The HTML table string representation of the <paramref name="instance"/>.</returns>
    private static Task<string> ToHtmlTableAsync(
        IEnumerable<Dictionary<string, object>> instance,
        Dictionary<string, object> tableAttributes,
        bool rowsOnly,
        HashSet<object> rendering,
        CancellationToken stoppingToken
    ) => WriteTableAsync(instance, instance, tableAttributes, rowsOnly, rendering, stoppingToken);

    /// <summary>
    ///     This method writes the table shared by every overload: a header row with one cell per
    ///     key, then one body row per dictionary with one cell per header.
    /// </summary>
    /// <param name="owner">
    ///     The object the rows came from. It is tracked in <paramref name="rendering" /> while its
    ///     table is written, so a value that points back at it is not expanded again.
    /// </param>
    /// <param name="rows">The row dictionaries, one per body row.</param>
    /// <param name="tableAttributes">Optional attributes for the HTML table element.</param>
    /// <param name="rowsOnly">Whether to write only the body rows.</param>
    /// <param name="rendering">The references being rendered now, for the cycle rule.</param>
    /// <param name="stoppingToken">Optional cancellation token to stop the operation.</param>
    /// <returns>The HTML string.</returns>
    private static async Task<string> WriteTableAsync(
        object owner,
        IEnumerable<Dictionary<string, object>> rows,
        Dictionary<string, object> tableAttributes,
        bool rowsOnly,
        HashSet<object> rendering,
        CancellationToken stoppingToken
    )
    {
        // Ensure the table attributes are not null.
        tableAttributes ??= [];

        // A blank key cannot head a column, so such entries are left out.
        Dictionary<string, object>[] bodyRows = rows.Select(d =>
                d.Where(kvp => kvp.Key?.Trim() is not (null or ""))
                    .ToDictionary(k => k.Key, v => v.Value)
            )
            .ToArray();

        // The header is the union of the keys in first-seen order.
        List<string> headers = bodyRows.SelectMany(d => d.Keys).Distinct().ToList();

        // Track the owner while its table is written, so a value that points back at it is written
        // as a type name instead of recursing forever.
        rendering.Add(owner);
        try
        {
            // Create our HTML memory stream.
            await using MemoryStream memoryStream = new();

            // Create our HTML writer using the memory stream and UTF-8 encoding.
            using HtmlWriter htmlWriter = new HtmlWriter(memoryStream, Encoding.UTF8);

            // If we're not just writing rows, we need to open the table and header elements.
            if (!rowsOnly)
            {
                // Open our table element.
                htmlWriter.WriteStartTag("table");

                // Iterate through the table attributes and write them to the HTML writer.
                foreach (var (key, value) in tableAttributes)
                    htmlWriter.WriteAttribute(key, value?.ToString() ?? string.Empty);

                // Open the table header element and its one row.
                htmlWriter.WriteStartTag("thead");
                htmlWriter.WriteStartTag("tr");

                // Write each key as a table header cell.
                foreach (string header in headers)
                {
                    htmlWriter.WriteStartTag("th");
                    htmlWriter.WriteText(header.Trim());
                    htmlWriter.WriteEndTag("th");
                }

                // Close the header row and element, then open the table body element.
                htmlWriter.WriteEndTag("tr");
                htmlWriter.WriteEndTag("thead");
                htmlWriter.WriteStartTag("tbody");
            }

            // Write one row per dictionary with one cell per header, in header order.
            foreach (Dictionary<string, object> row in bodyRows)
            {
                htmlWriter.WriteStartTag("tr");

                foreach (string header in headers)
                {
                    htmlWriter.WriteStartTag("td");

                    // A missing key leaves its cell empty.
                    if (row.TryGetValue(header, out object cell))
                        await WriteCellAsync(htmlWriter, cell, rendering, stoppingToken);

                    htmlWriter.WriteEndTag("td");
                }

                htmlWriter.WriteEndTag("tr");
            }

            // Close the table body and table elements if we're not just writing rows.
            if (!rowsOnly)
            {
                htmlWriter.WriteEndTag("tbody");
                htmlWriter.WriteEndTag("table");
            }

            // Flush the HTML writer to ensure all data is written to the memory stream.
            htmlWriter.Flush();

            // Reset the memory stream position to the beginning.
            memoryStream.Position = 0;

            // Convert the memory stream to a string using UTF-8 encoding.
            using StreamReader reader = new(memoryStream, Encoding.UTF8);

            // Return the HTML content as a string.
            return (await reader.ReadToEndAsync(stoppingToken)).Trim();
        }
        finally
        {
            rendering.Remove(owner);
        }
    }

    /// <summary>
    ///     This method writes one cell value: a nested table for a dictionary, a list of
    ///     dictionaries or a nested class, otherwise escaped text. The branches are exclusive, so a
    ///     nested dictionary is written once.
    /// </summary>
    /// <param name="htmlWriter">The writer positioned inside a <c>td</c> element.</param>
    /// <param name="value">The cell value.</param>
    /// <param name="rendering">The references being rendered now, for the cycle rule.</param>
    /// <param name="stoppingToken">Optional cancellation token to stop the operation.</param>
    private static async Task WriteCellAsync(
        HtmlWriter htmlWriter,
        object value,
        HashSet<object> rendering,
        CancellationToken stoppingToken
    )
    {
        // A reference that is already being rendered is a cycle; its type name ends the recursion.
        if (value is not null && !value.GetType().IsValueType && rendering.Contains(value))
            htmlWriter.WriteText(value.GetType().Name);
        // Check the type of value for a nested dictionary then write it as a nested HTML table.
        else if (value is Dictionary<string, object> nestedDictionary)
            htmlWriter.WriteMarkupText(
                await ToHtmlTableAsync(nestedDictionary, null, false, rendering, stoppingToken)
            );
        // Check the type of value for a list of dictionaries then write it as a nested HTML table.
        else if (value is IEnumerable<Dictionary<string, object>> nestedEnumerable)
            htmlWriter.WriteMarkupText(
                await ToHtmlTableAsync(nestedEnumerable, null, false, rendering, stoppingToken)
            );
        // Check for a nested class and write it as a nested HTML table. Test the property value's
        // type, not the enclosing instance's type, which always passes this check.
        else if (IsUserDefinedClass(value))
            htmlWriter.WriteMarkupText(
                await ToHtmlTableAsync(value, null, false, rendering, stoppingToken)
            );
        // Otherwise write the value as text.
        else
            htmlWriter.WriteText(value?.ToString()?.Trim() ?? string.Empty);
    }

    /// <summary>
    ///     This method determines whether a value is an instance of a user-defined class, which is
    ///     rendered from its public properties.
    /// </summary>
    /// <param name="value">The value to test; may be null.</param>
    /// <returns>True for a non-null class instance that is not a primitive, enum or sequence.</returns>
    private static bool IsUserDefinedClass(object value) =>
        value is not null
        && !value.GetType().IsPrimitive()
        && !value.GetType().IsEnumerableType()
        && value.GetType().IsClass
        && !value.GetType().IsEnum
        && !value.GetType().IsValueType;

    /// <summary>
    ///     This method renders a user-defined class from its public instance properties, skipping
    ///     indexers (which need an index argument).
    /// </summary>
    /// <param name="instance">The class instance.</param>
    /// <param name="tableAttributes">Optional attributes for the HTML table element.</param>
    /// <param name="rowOnly">Whether to write only the body row.</param>
    /// <param name="rendering">The references being rendered now, for the cycle rule.</param>
    /// <param name="stoppingToken">Optional cancellation token to stop the operation.</param>
    /// <returns>The HTML string.</returns>
    private static Task<string> ToHtmlTableAsync(
        object instance,
        Dictionary<string, object> tableAttributes,
        bool rowOnly,
        HashSet<object> rendering,
        CancellationToken stoppingToken
    )
    {
        // Localize the properties of the instance as a dictionary.
        Dictionary<string, object> properties = instance
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, p => p.GetValue(instance));

        return WriteTableAsync(
            instance,
            [properties],
            tableAttributes,
            rowOnly,
            rendering,
            stoppingToken
        );
    }

    /// <summary>
    ///     This extension method renders an object as an HTML table. A dictionary or list of
    ///     dictionaries is rendered directly; any other class is rendered from its public
    ///     properties.
    /// </summary>
    /// <param name="instance">The instance of the user-defined class to convert.</param>
    /// <param name="tableAttributes">Optional attributes for the HTML table element.</param>
    /// <param name="rowOnly">
    ///     Optional flag to indicate whether to write only the rows of
    ///     data for the table without the header or surrounding elements.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the operation.</param>
    /// <returns>The HTML table string representation of the <paramref name="instance"/>.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="instance"/> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     When <paramref name="instance"/> is not a user-defined class type.
    /// </exception>
    public static async Task<string> ToHtmlTableAsync(
        this object instance,
        Dictionary<string, object> tableAttributes = null,
        bool rowOnly = false,
        CancellationToken stoppingToken = default
    )
    {
        // Ensure the instance is not null.
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        // Reference equality, so two equal-looking objects are not mistaken for a cycle.
        HashSet<object> rendering = new(ReferenceEqualityComparer.Instance);

        // Check for a dictionary instance and convert it to an HTML table.
        if (instance is Dictionary<string, object> objectDictionary)
            return await ToHtmlTableAsync(
                objectDictionary,
                tableAttributes,
                rowOnly,
                rendering,
                stoppingToken
            );

        // Check for an enumerable instance and convert it to an HTML table.
        if (instance is IEnumerable<Dictionary<string, object>> enumerableDictionary)
            return await ToHtmlTableAsync(
                enumerableDictionary,
                tableAttributes,
                rowOnly,
                rendering,
                stoppingToken
            );

        // Ensure we're working with a user-defined class type.
        if (
            instance.GetType().IsPrimitive()
            || instance.GetType().IsEnumerableType()
            || !instance.GetType().IsClass
            || instance.GetType().IsEnum
            || instance.GetType().IsValueType
        )
            throw new ArgumentException(
                "The instance must be a user-defined class type.",
                nameof(instance)
            );

        return await ToHtmlTableAsync(instance, tableAttributes, rowOnly, rendering, stoppingToken);
    }
}
