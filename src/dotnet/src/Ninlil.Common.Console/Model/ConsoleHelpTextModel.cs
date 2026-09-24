using Ninlil.Common.Extension.Object;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class maintains the model for a console help text element, which
///     is used to organize the help text output for a console application.
/// </summary>
public class ConsoleHelpTextModel
{
    /// <summary>
    ///     This property is the internal container the description of the help text element.
    /// </summary>
    private string _description;

    /// <summary>
    ///     This property is the internal container for the group name of the help text element.
    /// </summary>
    private string _group;

    /// <summary>
    ///     This property is the internal container for the name of the help text element.
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
    ///     This property contains the position of the help text element in the overall help text output.
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
    ///     This method generates a detailed description for the help text element.
    /// </summary>
    /// <returns>A detailed description string for the help text element.</returns>
    public string GetHelpDescription(int padding = 0)
    {
        // Check for a verb.
        if (Verb)
            return IsDefault ? $"[default] {Description}" : Description;

        // Define our prefix list.
        List<string> prefixes = [$"Required: {Required.ToString().ToLower()}"];

        // Check for a required argument or a default value.
        if (Default is not null)
            prefixes.Add($"Default: {Default?.ToStringValue()}");

        // Check for a group.
        if (Group is not (null or ""))
            prefixes.Add($"Group: {Group}");

        // Localize our prefix string.
        string prefix = $"[{string.Join(", ", prefixes)}]";

        // Localize the description lines.
        string[] descriptionLines = Description.Contains(Environment.NewLine)
            ? Description.Split(Environment.NewLine)
            : [Description];

        // Localize our longest description line.
        int longest = descriptionLines.Max(d => d.Length);

        // Otherwise the description is shorter than the prefix, so pad the description.
        if (longest < prefix.Length)
            descriptionLines = descriptionLines.Select(d => d.PadLeft(prefix.Length)).ToArray();

        // Check for additional padding.
        if (padding > 0)
            descriptionLines = descriptionLines
                .Select(d => d.PadLeft(d.Length + padding + GetPadding()))
                .ToArray();

        // Prepend the prefix to the description lines.
        descriptionLines = descriptionLines.Prepend(prefix).ToArray();

        // We're done, generate and return our response.
        return string.Join(Environment.NewLine, descriptionLines);
    }

    /// <summary>
    ///     This method generates the name string for the help text element.
    /// </summary>
    /// <param name="padding">An optional padding value to apply to the name string.</param>
    /// <returns>The name string for the help text element.</returns>
    public string GetHelpName(int padding = 0)
    {
        // Check for a verb.
        if (Verb)
            return Name.PadRight(padding + GetPadding());

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
        return padding > 0 ? response.PadRight(padding + GetPadding()) : response;
    }

    /// <summary>
    ///     This method calculates the padding needed for the help text element based on its properties.
    /// </summary>
    /// <returns>The calculated padding value.</returns>
    public int GetPadding() => 4;

    /// <summary>
    ///     This method converts the instance into its string representation which is a help text line.
    /// </summary>
    /// <returns>The string representation of the instance.</returns>
    public override string ToString() => $"{GetHelpName()}:  {GetHelpDescription()}";

    /// <summary>
    ///     This method converts the instance into a tuple pair of its name and description.
    /// </summary>
    /// <param name="namePadding">An optional padding value to apply to the name string.</param>
    /// <param name="descriptionPadding">An optional padding value to apply to the description string.</param>
    /// <returns>A tuple pair containing the name and description of the instance.</returns>
    public Tuple<string, string> ToHelpStringPair(
        int namePadding = 0,
        int descriptionPadding = 0
    ) => new(GetHelpName(namePadding), GetHelpDescription(descriptionPadding));
}
