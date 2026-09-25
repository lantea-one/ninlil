// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This interface is the contract for a log prefix: an ordered list of parts, each shown in
///     brackets before a message. <see cref="ConsoleLoggerPrefixModel" /> implements it.
/// </summary>
public interface IConsoleLoggerPrefixModel : IList<string>
{
    /// <summary>
    ///     This method retrieves the index of a specified <paramref name="part" />
    ///     from within the <see cref="IConsoleLoggerPrefixModel" />.
    /// </summary>
    /// <param name="part">The part to retrieve the index of.</param>
    /// <returns>
    ///     The index of the specified <paramref name="part" /> or <see langword="-1" /> if it does not exist.
    /// </returns>
    public int GetPartIndex(string part);

    /// <summary>
    ///     This method retrieves the parts of the <see cref="IConsoleLoggerPrefixModel" /> as a list
    ///     of strings, optionally <paramref name="formatted" /> with <paramref name="padding" />.
    /// </summary>
    /// <param name="formatted">
    ///     A <see cref="bool" /> indicating whether the parts should be formatted with padding.
    /// </param>
    /// <param name="padding">
    ///     An <see cref="int" /> specifying the amount of padding to apply to each part when formatting.
    /// </param>
    /// <returns>
    ///     A <see cref="List{String}" /> representing the parts of the <see cref="IConsoleLoggerPrefixModel" />,
    ///     <paramref name="formatted" /> with the specified <paramref name="padding" />.
    /// </returns>
    public List<string> GetParts(bool formatted = false, int padding = 0);

    /// <summary>
    ///     This method merges the parts from an <paramref name="other" /> <see cref="ConsoleLoggerPrefixModel" />
    ///     into <see langword="this" /> instance, ensuring that only unique and non-empty parts are added, and
    ///     preserving the order of existing parts.
    /// </summary>
    /// <param name="other">
    ///     The other <see cref="ConsoleLoggerPrefixModel" /> to merge into <see langword="this" /> instance.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerPrefixModel" />
    ///     with the parts from the <paramref name="other" /> merged in.
    /// </returns>
    public IConsoleLoggerPrefixModel Merge(IConsoleLoggerPrefixModel other);

    /// <summary>
    ///     This method returns a string representation of the
    ///     <see cref="IConsoleLoggerPrefixModel" />, optionally formatted with <paramref name="padding" />.
    /// </summary>
    /// <param name="padding">The padding to apply to each part when formatting the string representation.</param>
    /// <returns>
    ///     The string representation of the <see cref="IConsoleLoggerPrefixModel" />,
    ///     formatted with the specified <paramref name="padding" />.
    /// </returns>
    public string ToString(int padding);

    /// <summary>
    ///     This method returns a string representation of the <see cref="IConsoleLoggerPrefixModel" />
    ///     without any additional formatting or padding.
    /// </summary>
    /// <returns>
    ///     The string representation of the <see cref="IConsoleLoggerPrefixModel" />
    ///     without any additional formatting or padding.
    /// </returns>
    public string ToString();

    /// <summary>
    ///     This method removes a <paramref name="part" /> from the <see cref="IConsoleLoggerPrefixModel" />.
    /// </summary>
    /// <param name="part">
    ///     The part text to remove, or an <see cref="int" /> index. A negative index removes that many
    ///     parts from the end.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerPrefixModel" /> with the specified part removed.
    /// </returns>
    public IConsoleLoggerPrefixModel WithoutPart(object part);

    /// <summary>
    ///     This method adds a new <paramref name="part" /> to the <see cref="IConsoleLoggerPrefixModel" />,
    ///     ensuring that it is sanitized and non-empty before adding it to the model.
    /// </summary>
    /// <param name="part">
    ///     The part to add to the <see cref="IConsoleLoggerPrefixModel" />.
    ///     It will be sanitized and validated before being added.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerPrefixModel" /> with the new part added.
    /// </returns>
    public IConsoleLoggerPrefixModel WithPart(string part);

    /// <summary>
    ///     This method adds multiple new <paramref name="parts" /> to the <see cref="IConsoleLoggerPrefixModel" />,
    ///     ensuring that each part is sanitized and non-empty before adding it to the model.
    /// </summary>
    /// <param name="parts">
    ///     The array of parts to add to the <see cref="IConsoleLoggerPrefixModel" />.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerPrefixModel" /> with the new parts added.
    /// </returns>
    public IConsoleLoggerPrefixModel WithParts(params string[] parts);
}
