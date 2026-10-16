using Ninlil.Common.Extension.Object;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class is one entry in the help text: a verb, a positional argument or a named
///     argument. The console attributes create it through their <c>ToHelpTextModel</c> methods,
///     and <see cref="ConsoleHelpTextCollectionModel" /> lays the entries out in columns.
/// </summary>
public class ConsoleHelpTextModel
{
    /// <summary>
    ///     This field holds the description of the help text element.
    /// </summary>
    private string _description;

    /// <summary>
    ///     This field holds the group name of the help text element.
    /// </summary>
    private string _group;

    /// <summary>
    ///     This field holds the name of the help text element.
    /// </summary>
    private string _name;

    /// <summary>
    ///     This property contains the default value of the help text element.
    /// </summary>
    public object Default { get; set; }

    /// <summary>
    ///     This property contains the description of the help text element.
    /// </summary>
    public string Description
    {
        get => _description?.Trim();
        set => _description = value?.Trim();
    }

    /// <summary>
    ///     This property contains the group name for the help text
    ///     element which can be used to categorize related elements.
    /// </summary>
    public string Group
    {
        get => _group?.Trim();
        set => _group = value?.Trim();
    }

    /// <summary>
    ///     This property indicates whether this help text element is the default element or not.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    ///     This property contains the name of the help text element.
    /// </summary>
    public string Name
    {
        get => _name?.Trim();
        set => _name = value?.Trim();
    }

    /// <summary>
    ///     This property contains the argument position for a positional argument. A negative value
    ///     (the default is <see cref="int.MinValue" />) marks a named argument or verb.
    /// </summary>
    public int Position { get; set; } = int.MinValue;

    /// <summary>
    ///     This property indicates whether this help text element is required or not.
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    ///     This property contains the short name (single character) of the help text element.
    /// </summary>
    public char? ShortName { get; set; }

    /// <summary>
    ///     This property indicates whether this help text element is for a verb or not.
    /// </summary>
    public bool Verb { get; set; }

    /// <summary>
    ///     This method builds the description column for the entry. A verb shows its description,
    ///     marked when it is the default. An argument shows a bracketed line with its required flag,
    ///     default and group, followed by the description lines.
    /// </summary>
    /// <param name="padding">
    ///     The number of spaces that start every description line after the first (the bracketed
    ///     line of an argument follows the name column), so continuation lines begin at the
    ///     description column. Zero adds no indentation.
    /// </param>
    /// <returns>The description column text for the help text element.</returns>
    public string GetHelpDescription(int padding = 0)
    {
        // Check for a verb.
        if (Verb)
            return IsDefault
                ? string.IsNullOrEmpty(Description)
                    ? "[default]"
                    : $"[default] {Description}"
                : Description;

        // Define our prefix list.
        List<string> prefixes = [$"Required: {Required.ToString().ToLower()}"];

        // Check for a default value.
        if (Default is not null)
            prefixes.Add($"Default: {Default?.ToStringValue()}");

        // Check for a group.
        if (Group is not (null or ""))
            prefixes.Add($"Group: {Group}");

        // Localize our prefix string.
        string prefix = $"[{string.Join(", ", prefixes)}]";

        // An argument without a description shows the bracketed line alone.
        if (string.IsNullOrEmpty(Description))
            return prefix;

        // Localize the description lines.
        string[] descriptionLines = Description.Split(Environment.NewLine);

        // Localize our longest description line.
        int longest = descriptionLines.Max(d => d.Length);

        // Right-align a description that is shorter than the bracketed line.
        if (longest < prefix.Length)
            descriptionLines = descriptionLines.Select(d => d.PadLeft(prefix.Length)).ToArray();

        // Indent every description line by exactly the padding, so continuation lines start at
        // the description column. The prefix line is not indented: it follows the name column.
        if (padding > 0)
            descriptionLines = descriptionLines
                .Select(d => d.PadLeft(d.Length + padding))
                .ToArray();

        // Prepend the prefix to the description lines.
        descriptionLines = descriptionLines.Prepend(prefix).ToArray();

        // We're done, generate and return our response.
        return string.Join(Environment.NewLine, descriptionLines);
    }

    /// <summary>
    ///     This method builds the name column for the entry: the verb name, <c>&lt;position&gt;</c>
    ///     for a positional argument, or <c>-x, --name</c> for a named argument. Optional arguments
    ///     are wrapped in brackets.
    /// </summary>
    /// <param name="padding">
    ///     The width to pad the name to, so a column of names lines up. Zero returns the name
    ///     unpadded; a name longer than the padding is not cut.
    /// </param>
    /// <returns>The name string for the help text element.</returns>
    public string GetHelpName(int padding = 0)
    {
        // Check for a verb.
        if (Verb)
            return Name.PadRight(padding);

        // Define our response.
        string response;

        // Check for a positional argument.
        if (Position >= 0)
            response = $"<{Position}>";
        // Otherwise, we've got a named argument.
        else
            response = ShortName is null ? $"--{Name}" : $"-{ShortName}, --{Name}";

        // Check for a required argument.
        if (!Required)
            response = $"[{response}]";

        // We're done, return the response.
        return padding > 0 ? response.PadRight(padding) : response;
    }

    /// <summary>
    ///     This method returns the gap between the name and description columns, always four spaces.
    /// </summary>
    /// <returns>The column gap.</returns>
    public int GetPadding() => 4;

    /// <summary>
    ///     This method converts the instance into its string representation which is a help text line.
    /// </summary>
    /// <returns>The string representation of the instance.</returns>
    public override string ToString() => $"{GetHelpName()}:  {GetHelpDescription()}";

    /// <summary>
    ///     This method converts the instance into a tuple pair of its name and description.
    /// </summary>
    /// <param name="namePadding">The width to pad the name to, see <see cref="GetHelpName" />.</param>
    /// <param name="descriptionPadding">
    ///     The indentation of continuation lines, see <see cref="GetHelpDescription" />.
    /// </param>
    /// <returns>A tuple pair containing the name and description of the instance.</returns>
    public Tuple<string, string> ToHelpStringPair(
        int namePadding = 0,
        int descriptionPadding = 0
    ) => new(GetHelpName(namePadding), GetHelpDescription(descriptionPadding));
}
