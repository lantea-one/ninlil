using System.Reflection;
using System.Text;
using MimeKit.Text;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Object;

/// <summary>
///     This static class renders an object, a <c>Dictionary&lt;string, object&gt;</c> or a list of
///     such dictionaries as an HTML table using MimeKit's <see cref="HtmlWriter" />. Public
///     properties (or dictionary keys) become the header cells; nested classes and dictionaries
///     become nested tables. Each value is written in its own body row with a single cell.
/// </summary>
public static class ToHtmlTableObjectExtension
{
    /// <summary>
    ///     This method renders a dictionary as an HTML table: the keys as header cells, then one row
    ///     per value.
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
    /// <param name="stoppingToken">Optional cancellation token to stop the operation.</param>
    /// <returns>A string representation of the <paramref name="instance" /> as an HTML table.</returns>
    private static async Task<string> ToHtmlTableAsync(
        Dictionary<string, object> instance,
        Dictionary<string, object> tableAttributes = null,
        bool rowOnly = false,
        CancellationToken stoppingToken = default
    )
    {
        // Ensure the table attributes are not null.
        tableAttributes ??= [];

        // Create our HTML memory stream.
        await using MemoryStream memoryStream = new();

        // Create our HTML writer using the memory stream and UTF-8 encoding.
        using HtmlWriter htmlWriter = new HtmlWriter(memoryStream, Encoding.UTF8);

        // If we're not just writing rows, we need to open the table and header elements.
        if (!rowOnly)
        {
            // Open our header element.
            htmlWriter.WriteStartTag("table");

            // Iterate through the table attributes and write them to the HTML writer.
            foreach (var (key, value) in tableAttributes)
                htmlWriter.WriteAttribute(key, value?.ToString() ?? string.Empty);

            // Open the table header element.
            htmlWriter.WriteStartTag("thead");

            // Open the table header row element.
            htmlWriter.WriteStartTag("tr");

            // Iterate through the keys and write them as table header cells.
            foreach (var key in instance.Keys.Where(k => k?.Trim() is not (null or "")))
            {
                // Open the table header cell element.
                htmlWriter.WriteStartTag("th");

                // Write the key as text in the table header cell.
                htmlWriter.WriteText(key.Trim());

                // Close the table header cell element.
                htmlWriter.WriteEndTag("th");
            }

            // Close the table header row element.
            htmlWriter.WriteEndTag("tr");

            // Close the table header element.
            htmlWriter.WriteEndTag("thead");

            // Open the table body element.
            htmlWriter.WriteStartTag("tbody");
        }

        // Iterate through the key-value pairs in the dictionary.
        foreach (
            (object _, object value) in instance.Where(kvp => kvp.Key?.Trim() is not (null or ""))
        )
        {
            // Open the table row element.
            htmlWriter.WriteStartTag("tr");

            // Open the table cell element for the value.
            htmlWriter.WriteStartTag("td");

            // Check the type of value for a nested dictionary then write it as a nested HTML table.
            if (value is Dictionary<string, object> dictionary)
                htmlWriter.WriteMarkupText(
                    await ToHtmlTableAsync(dictionary, null, false, stoppingToken)
                );

            // Check the type of value for a list of dictionaries then write it as a nested HTML table.
            // This is not an else-if, so a nested dictionary also reaches the else branch below and
            // is written a second time as text.
            if (value is IEnumerable<Dictionary<string, object>> enumerable)
                htmlWriter.WriteMarkupText(
                    await ToHtmlTableAsync(enumerable, null, false, stoppingToken)
                );
            // Otherwise write the value as text.
            else
                htmlWriter.WriteText(value?.ToString()?.Trim() ?? string.Empty);

            // Close the table cell element for the value.
            htmlWriter.WriteEndTag("td");

            // Close the table row element.
            htmlWriter.WriteEndTag("tr");
        }

        // Close the table body and table elements if we're not just writing rows.
        if (!rowOnly)
        {
            // Close the table body element.
            htmlWriter.WriteEndTag("tbody");

            // Close the table element.
            htmlWriter.WriteEndTag("table");
        }

        // Flush the HTML writer to ensure all data is written to the memory stream.
        htmlWriter.Flush();

        // Reset the memory stream position to the beginning.
        memoryStream.Position = 0;

        // Convert the memory stream to a string using UTF-8 encoding.
        using StreamReader reader = new(memoryStream, Encoding.UTF8);

        // Read the entire content of the memory stream asynchronously.
        string htmlContent = await reader.ReadToEndAsync(stoppingToken);

        // Return the HTML content as a string.
        return htmlContent.Trim();
    }

    /// <summary>
    ///     This method renders a list of dictionaries as one HTML table, taking the header cells from
    ///     the first dictionary's keys and writing each dictionary's rows in turn.
    /// </summary>
    /// <param name="instance">The instance of the <see cref="IEnumerable{Dictionary}"/> to convert.</param>
    /// <param name="tableAttributes">Optional attributes for the HTML table element.</param>
    /// <param name="rowsOnly">
    ///     Optional flag to indicate whether to write only the rows of
    ///     data for the table without the header or surrounding elements.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the operation.</param>
    /// <returns>The HTML table string representation of the <paramref name="instance"/>.</returns>
    private static async Task<string> ToHtmlTableAsync(
        IEnumerable<Dictionary<string, object>> instance,
        Dictionary<string, object> tableAttributes = null,
        bool rowsOnly = false,
        CancellationToken stoppingToken = default
    )
    {
        // Ensure the table attributes are not null.
        tableAttributes ??= [];

        // Localize the array of dictionaries to a list for easier access.
        Dictionary<string, object>[] dictionaries = instance
            .Select(d =>
                d.Where(kvp => kvp.Key?.Trim() is not (null or ""))
                    .ToDictionary(k => k.Key, v => v.Value)
            )
            .ToArray();

        // Create our HTML memory stream.
        await using MemoryStream memoryStream = new();

        // Create our HTML writer using the memory stream and UTF-8 encoding.
        using HtmlWriter htmlWriter = new HtmlWriter(memoryStream, Encoding.UTF8);

        // If we're not just writing rows, we need to open the table and header elements.
        if (!rowsOnly)
        {
            // Open our header element.
            htmlWriter.WriteStartTag("table");

            // Iterate through the table attributes and write them to the HTML writer.
            foreach (var (key, value) in tableAttributes)
                htmlWriter.WriteAttribute(key, value?.ToString() ?? string.Empty);

            // Open the table header element.
            htmlWriter.WriteStartTag("thead");

            // Open the table header row element.
            htmlWriter.WriteStartTag("tr");

            // Iterate through the keys and write them as table header cells.
            foreach (
                (string key, object _) in dictionaries
                    .FirstOrDefault()
                    ?.Where(kvp => kvp.Key?.Trim() is not (null or ""))
                    ?? []
            )
            {
                // Open the table header cell element.
                htmlWriter.WriteStartTag("th");

                // Write the key as text in the table header cell.
                htmlWriter.WriteText(key.Trim());

                // Close the table header cell element.
                htmlWriter.WriteEndTag("th");
            }

            // Close the table header row element.
            htmlWriter.WriteEndTag("tr");

            // Close the table header element.
            htmlWriter.WriteEndTag("thead");

            // Open the table body element.
            htmlWriter.WriteStartTag("tbody");
        }

        // Iterate through the dictionaries
        foreach (Dictionary<string, object> dictionary in dictionaries)
            htmlWriter.WriteMarkupText(
                await ToHtmlTableAsync(dictionary, null, true, stoppingToken)
            );

        // Close the table body and table elements if we're not just writing rows.
        if (!rowsOnly)
        {
            // Close the table body element.
            htmlWriter.WriteEndTag("tbody");

            // Close the table element.
            htmlWriter.WriteEndTag("table");
        }

        // Flush the HTML writer to ensure all data is written to the memory stream.
        htmlWriter.Flush();

        // Reset the memory stream position to the beginning.
        memoryStream.Position = 0;

        // Convert the memory stream to a string using UTF-8 encoding.
        using StreamReader reader = new(memoryStream, Encoding.UTF8);

        // Read the entire content of the memory stream asynchronously.
        string htmlContent = await reader.ReadToEndAsync(stoppingToken);

        // Return the HTML content as a string.
        return htmlContent.Trim();
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

        // Check for a dictionary instance and convert it to an HTML table.
        if (instance is Dictionary<string, object> objectDictionary)
            return await ToHtmlTableAsync(
                objectDictionary,
                tableAttributes,
                rowOnly,
                stoppingToken
            );

        // Check for an enumerable instance and convert it to an HTML table.
        if (instance is IEnumerable<Dictionary<string, object>> enumerableDictionary)
            return await ToHtmlTableAsync(
                enumerableDictionary,
                tableAttributes,
                rowOnly,
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

        // Ensure the table attributes are not null.
        tableAttributes ??= [];

        // Create our HTML memory stream.
        await using MemoryStream memoryStream = new();

        // Create our HTML writer using the memory stream and UTF-8 encoding.
        using HtmlWriter htmlWriter = new HtmlWriter(memoryStream, Encoding.UTF8);

        // Localize the properties of the instance as a dictionary.
        Dictionary<string, object> dictionary = instance
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => new KeyValuePair<string, object>(p.Name, p.GetValue(instance)))
            .ToDictionary(k => k.Key, v => v.Value);

        // If we're not just writing rows, we need to open the table and header elements.
        if (!rowOnly)
        {
            // Open our header element.
            htmlWriter.WriteStartTag("table");

            // Iterate through the table attributes and write them to the HTML writer.
            foreach (var (key, value) in tableAttributes)
                htmlWriter.WriteAttribute(key, value?.ToString() ?? string.Empty);

            // Open the table header element.
            htmlWriter.WriteStartTag("thead");

            // Open the table header row element.
            htmlWriter.WriteStartTag("tr");

            // Iterate through the keys and write them as table header cells.
            foreach (
                (string key, object _) in dictionary.Where(k => k.Key?.Trim() is not (null or ""))
            )
            {
                // Open the table header cell element.
                htmlWriter.WriteStartTag("th");

                // Write the key as text in the table header cell.
                htmlWriter.WriteText(key.Trim());

                // Close the table header cell element.
                htmlWriter.WriteEndTag("th");
            }

            // Close the table header row element.
            htmlWriter.WriteEndTag("tr");

            // Close the table header element.
            htmlWriter.WriteEndTag("thead");

            // Open the table body element.
            htmlWriter.WriteStartTag("tbody");
        }

        // Iterate through the key-value pairs in the dictionary.
        foreach (
            (string _, object value) in dictionary.Where(kvp =>
                kvp.Key?.ToString().Trim() is not (null or "")
            )
        )
        {
            // Open the table row element.
            htmlWriter.WriteStartTag("tr");

            // Open the table cell element for the value.
            htmlWriter.WriteStartTag("td");

            // Check the type of value for a nested dictionary then write it as a nested HTML table.
            if (value is Dictionary<string, object> nestedDictionary)
                htmlWriter.WriteMarkupText(
                    await ToHtmlTableAsync(nestedDictionary, null, false, stoppingToken)
                );
            // Check the type of value for a list of dictionaries then write it as a nested HTML table.
            else if (value is IEnumerable<Dictionary<string, object>> nestedEnumerable)
                htmlWriter.WriteMarkupText(
                    await ToHtmlTableAsync(nestedEnumerable, null, false, stoppingToken)
                );
            // Check for a nested class and write it as a nested HTML table. Test the property value's
            // type, not the enclosing instance's type, which always passes this check.
            else if (
                value is not null
                && !value.GetType().IsPrimitive()
                && !value.GetType().IsEnumerableType()
                && value.GetType().IsClass
                && !value.GetType().IsEnum
                && !value.GetType().IsValueType
            )
                htmlWriter.WriteMarkupText(
                    await ToHtmlTableAsync(value, null, false, stoppingToken)
                );
            // Otherwise write the value as text.
            else
                htmlWriter.WriteText(value?.ToString()?.Trim() ?? string.Empty);

            // Close the table cell element for the value.
            htmlWriter.WriteEndTag("td");

            // Close the table row element.
            htmlWriter.WriteEndTag("tr");
        }

        // Close the table body and table elements if we're not just writing rows.
        if (!rowOnly)
        {
            // Close the table body element.
            htmlWriter.WriteEndTag("tbody");

            // Close the table element.
            htmlWriter.WriteEndTag("table");
        }

        // Flush the HTML writer to ensure all data is written to the memory stream.
        htmlWriter.Flush();

        // Reset the memory stream position to the beginning.
        memoryStream.Position = 0;

        // Convert the memory stream to a string using UTF-8 encoding.
        using StreamReader reader = new(memoryStream, Encoding.UTF8);

        // Read the entire content of the memory stream asynchronously.
        string htmlContent = await reader.ReadToEndAsync(stoppingToken);

        // Return the HTML content as a string.
        return htmlContent.Trim();
    }
}
