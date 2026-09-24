// Define our namespace for the console logger prefix model.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class represents a model for managing console logger prefixes,
///     allowing for the addition, removal, and formatting of prefix parts.
/// </summary>
public class ConsoleLoggerPrefixModel : List<string>, IConsoleLoggerPrefixModel
{
    /// <summary>
    ///     This operator implicitly converts an array of strings into a <see cref="ConsoleLoggerPrefixModel" />.
    /// </summary>
    /// <param name="collection">
    ///     The <see cref="Array" /> of <see cref="string" /> to convert into a <see cref="ConsoleLoggerPrefixModel" />.
    /// </param>
    /// <returns>
    ///     The <see cref="ConsoleLoggerPrefixModel" /> representation of the <paramref name="collection" />
    /// </returns>
    public static implicit operator ConsoleLoggerPrefixModel(string[] collection)
    {
        // Create a new instance of the console logger prefix model.
        ConsoleLoggerPrefixModel instance = new();

        // If the provided collection is null or empty then we'll return an empty instance.
        if (collection is not { Length: > 0 })
            return instance;

        // Iterate through the items in the incoming collection.
        foreach (
            var item in collection
                .Select(i => ConsoleLogger.GetSanitizedPart(i))
                .Where(i => i?.Trim() is not (null or { Length: 0 }))
        )
            instance.WithPart(item);

        // We're done, return the newly populated instance of the console logger prefix model.
        return instance;
    }

    /// <summary>
    ///     This constructor instantiates a new instance of the <see cref="ConsoleLoggerPrefixModel" /> class.
    /// </summary>
    public ConsoleLoggerPrefixModel() { }

    /// <summary>
    ///     This constructor instantiates a new instance of the <see cref="ConsoleLoggerPrefixModel" />
    ///     class with a specified initial <paramref name="capacity" />
    /// </summary>
    /// <param name="capacity">
    ///     The initial number of elements that the <see cref="ConsoleLoggerPrefixModel" /> can contain.
    /// </param>
    public ConsoleLoggerPrefixModel(int capacity)
        : base(capacity) { }

    /// <summary>
    ///     This constructor instantiates a new instance of the <see cref="ConsoleLoggerPrefixModel" />
    ///     from a provided existing <paramref name="collection" /> of strings.
    /// </summary>
    /// <param name="collection">
    ///     The collection of strings to initialize the <see cref="ConsoleLoggerPrefixModel" /> with.
    /// </param>
    public ConsoleLoggerPrefixModel(IEnumerable<string> collection)
        : base(collection) { }

    /// <summary>
    ///     This method retrieves the index of a specified <paramref name="part" />
    ///     from within the <see cref="ConsoleLoggerPrefixModel" />.
    /// </summary>
    /// <param name="part">The part to retrieve the index of.</param>
    /// <returns>
    ///     The index of the specified <paramref name="part" /> or <see langword="-1" /> if it does not exist.
    /// </returns>
    public int GetPartIndex(string part) =>
        ConsoleLogger.GetSanitizedPart(part) is null or { Length: 0 }
            ? -1
            : GetParts().IndexOf(ConsoleLogger.GetSanitizedPart(part));

    /// <summary>
    ///     This method retrieves the parts of the <see cref="ConsoleLoggerPrefixModel" /> as a list
    ///     of strings, optionally <paramref name="formatted" /> with <paramref name="padding" />.
    /// </summary>
    /// <param name="formatted">
    ///     A <see cref="bool" /> indicating whether the parts should be formatted with padding.
    /// </param>
    /// <param name="padding">
    ///     An <see cref="int" /> specifying the amount of padding to apply to each part when formatting.
    /// </param>
    /// <returns>
    ///    A <see cref="List{String}" /> of representing the parts of the <see cref="ConsoleLoggerPrefixModel" />,
    ///     <paramref name="formatted" /> with the specified <paramref name="padding" />.
    /// </returns>
    public List<string> GetParts(bool formatted = false, int padding = 0) =>
        this.Select(p => ConsoleLogger.GetSanitizedPart(p))
            .Where(p => p is not (null or { Length: 0 }))
            .Select(p =>
            {
                // If we're not formatting the parts, just return the sanitized part.
                if (!formatted)
                    return p;

                // If the part is a date/time value, we don't want to pad it, so we'll just return it as is.
                if (DateTime.TryParse(p, out _))
                    return $"[{p}]";

                // If the padding is less than 0 then we'll need to calculate
                // the maximum length of the parts that are not date/time values.
                if (padding < 0)
                    padding = this.Select(p => ConsoleLogger.GetSanitizedPart(p))
                        .Where(pp => pp is not (null or { Length: 0 }))
                        .Where(pp => !DateTime.TryParse(pp, out _))
                        .Select(pp => pp.Length)
                        .DefaultIfEmpty(0)
                        .Max();

                // Calculate the difference between the padding and the length of the part.
                int difference =
                    padding - p.Length <= 0 ? 0 : (int)Math.Floor((double)(padding - p.Length) / 2);

                // Pad the part with spaces on the left and right to center it within the specified padding.
                p = (new string(' ', difference) + p).PadRight(padding);

                // We're done, return the formatted part.
                return $"[{p}]";
            })
            .ToList();

    /// <summary>
    ///     This method merges the parts from an <paramref name="other" /> <see cref="ConsoleLoggerPrefixModel" />
    ///     into <see langword="this" /> instance, ensuring that only unique and non-empty parts are added, and
    ///     preserving the order of existing parts.
    /// </summary>
    /// <param name="other">
    ///     The other <see cref="ConsoleLoggerPrefixModel" /> to merge into <see langword="this" /> instance.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="ConsoleLoggerPrefixModel" />
    ///     with the parts from the <paramref name="other" /> merged in.
    /// </returns>
    public IConsoleLoggerPrefixModel Merge(IConsoleLoggerPrefixModel other)
    {
        // If the other model is null or empty, return the current instance.
        if (other is null or { Count: 0 })
            return this;

        // Iterate through the parts of the other model, and add them to the current instance if they are
        // not null or empty, and if they do not already exist anywhere in the current instance.
        foreach (
            var part in other
                .Where(o => o is not (null or { Length: 0 }))
                .Where(o => GetPartIndex(o) == -1)
        )
            WithPart(part);

        // We're done, return the current instance with the other instance's parts merged in.
        return this;
    }

    /// <summary>
    ///     This method returns a string representation of the
    ///     <see cref="ConsoleLoggerPrefixModel" />, optionally formatted with <paramref name="padding" />.
    /// </summary>
    /// <param name="padding">The padding to apply to each part when formatting the string representation.</param>
    /// <returns>
    ///     The string representation of the <see cref="ConsoleLoggerPrefixModel" />,
    ///     formatted with the specified <paramref name="padding" />.
    /// </returns>
    public string ToString(int padding) => string.Join(' ', GetParts(true, padding));

    /// <summary>
    ///     This method returns a string representation of the <see cref="ConsoleLoggerPrefixModel" />
    ///     without any additional formatting or padding.
    /// </summary>
    /// <returns>
    ///     The string representation of the <see cref="ConsoleLoggerPrefixModel" />
    ///     without any additional formatting or padding.
    /// </returns>
    public override string ToString() => ToString(0);

    /// <summary>
    ///     This method removes a specified <paramref name="part" /> from the <see cref="ConsoleLoggerPrefixModel" />,
    ///     ensuring that the part is sanitized and non-empty before attempting to remove it.
    /// </summary>
    /// <param name="part">The part to remove from the <see cref="ConsoleLoggerPrefixModel" />.</param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerPrefixModel" /> with the specified part removed.
    /// </returns>
    public IConsoleLoggerPrefixModel WithoutPart(object part)
    {
        // Check for a numerical index to remove a part by its index,
        // supporting negative indices for reverse order removal.
        if (part is int intPart)
        {
            // If the part is a negative index, calculate the corresponding
            // positive index and remove the part if it exists.
            if (intPart < 0)
            {
                // Calculate the starting index for removal based on the negative index provided.
                var startIndex = Count + intPart;

                // Calculate the number of parts to remove based on the negative index.
                var countToRemove = -intPart;

                // If the starting index is valid and the range to remove
                // is within bounds, remove the specified range of parts.
                if (startIndex >= 0 && startIndex + countToRemove <= Count)
                    RemoveRange(startIndex, countToRemove);

                // We're done, return the current instance of the
                // ConsoleLoggerPrefixModel after removing the specified parts.
                return this;
            }

            // Remove the part at the specified index if it is within bounds.
            RemoveAt(intPart);

            // We're done, return the current instance of the ConsoleLoggerPrefixModel after removing the specified part.
            return this;
        }

        // Check for a string part to remove it from the model if it exists and is non-empty.
        if (part is string stringPart && stringPart.Trim() is not { Length: 0 })
        {
            // Localize the index of the part to remove it from the model if it exists.
            var index = this.GetPartIndex(part.ToString());

            // If the index is valid (greater than -1), remove the part at that index from the model.
            if (index > -1)
                RemoveAt(index);

            // We're done, return the current instance of the
            // ConsoleLoggerPrefixModel after removing the specified part.
            return this;
        }

        // We're done, return the current instance of the ConsoleLoggerPrefixModel without
        // any modifications if the part was neither a valid index nor a non-empty string.
        return this;
    }

    /// <summary>
    ///     This method adds a new <paramref name="part" /> to the <see cref="ConsoleLoggerPrefixModel" />,
    ///     ensuring that it is sanitized and non-empty before adding it to the model.
    /// </summary>
    /// <param name="part">
    ///     The part to add to the <see cref="ConsoleLoggerPrefixModel" />.
    ///     It will be sanitized and validated before being added.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerPrefixModel" /> with the new part added.
    /// </returns>
    public IConsoleLoggerPrefixModel WithPart(string part)
    {
        // Localize the sanitization of the part to ensure it is valid and non-empty before adding it to the model.
        var sanitizedPart = ConsoleLogger.GetSanitizedPart(part);

        // If the sanitized part is null or empty, we do not add it to the model and simply return the current instance.
        if (sanitizedPart is null or { Length: 0 })
            return this;

        // Otherwise, we'll add the sanitized part to the model and return the current instance for method chaining.
        Add(sanitizedPart);

        // We're done, return the updated instance of the ConsoleLoggerPrefixModel with the new part added.
        return this;
    }

    /// <summary>
    ///     This method adds multiple new <paramref name="parts" /> to the <see cref="ConsoleLoggerPrefixModel" />,
    ///     ensuring that each part is sanitized and non-empty before adding it to the model.
    /// </summary>
    /// <param name="parts">
    ///     The array of parts to add to the <see cref="ConsoleLoggerPrefixModel" />.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerPrefixModel" /> with the new parts added.
    /// </returns>
    public IConsoleLoggerPrefixModel WithParts(params string[] parts)
    {
        // If the provided parts array is null or empty, we do not add
        // any parts to the model and simply return the current instance.
        if (parts is null or { Length: 0 })
            return this;

        // Iterate through the provided parts array and add each part to the model
        // using the WithPart method, which handles sanitization and validation.
        foreach (var part in parts)
            WithPart(part);

        // We're done, return the updated instance of the ConsoleLoggerPrefixModel with the new parts added.
        return this;
    }
}
