using System.Text;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class builds the help text for a console application. It holds the application
///     details (defaulting to the static values on <see cref="ConsoleCommand" />) and a list of
///     <see cref="ConsoleHelpTextModel" /> entries, and writes them as an information block
///     followed by verb, positional-argument and named-argument sections, each with a banner
///     header. <see cref="ConsoleCommandInputModel.GetHelpTextCollection(Type, ConsoleHelpTextCollectionModel)" />
///     fills it from an input model's attributes.
/// </summary>
public class ConsoleHelpTextCollectionModel : List<ConsoleHelpTextModel>
{
    /// <summary>
    ///     This property contains the indentation used in the help text: four spaces.
    /// </summary>
    public static string Tab => string.Join("", Enumerable.Repeat(" ", 4));

    /// <summary>
    ///     This field holds the application name.
    /// </summary>
    private string _application = ConsoleCommand.Application;

    /// <summary>
    ///     This field holds the application author.
    /// </summary>
    private string _author = ConsoleCommand.Author;

    /// <summary>
    ///     This field holds the company.
    /// </summary>
    private string _company = ConsoleCommand.Company;

    /// <summary>
    ///     This field holds the application description.
    /// </summary>
    private string _description = ConsoleCommand.Description;

    /// <summary>
    ///     This field holds the application version.
    /// </summary>
    private string _version = ConsoleCommand.Version;

    /// <summary>
    ///     This property contains the name of the application.
    /// </summary>
    public string Application
    {
        get => _application?.Trim();
        set => _application = value?.Trim();
    }

    /// <summary>
    ///     This property contains the author of the application.
    /// </summary>
    public string Author
    {
        get => _author?.Trim();
        set => _author = value?.Trim();
    }

    /// <summary>
    ///     This property contains the company that produced the application.
    /// </summary>
    public string Company
    {
        get => _company?.Trim();
        set => _company = value?.Trim();
    }

    /// <summary>
    ///     This property contains the description of the application.
    /// </summary>
    public string Description
    {
        get => _description?.Trim();
        set => _description = value?.Trim();
    }

    /// <summary>
    ///     This property contains the version of the application.
    /// </summary>
    public string Version
    {
        get => _version?.Trim();
        set => _version = value?.Trim();
    }

    /// <summary>
    ///     This is the default constructor for the <see cref="ConsoleHelpTextCollectionModel" /> class.
    /// </summary>
    public ConsoleHelpTextCollectionModel() { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleHelpTextCollectionModel" />
    ///     class that is empty and has the specified initial <paramref name="capacity" />.
    /// </summary>
    /// <param name="capacity">The number of elements that the new list can initially store.</param>
    public ConsoleHelpTextCollectionModel(int capacity)
        : base(capacity) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleHelpTextCollectionModel" />
    ///     class that contains elements copied from the specified <paramref name="collection" />
    ///     and has sufficient capacity to accommodate the number of elements copied.
    /// </summary>
    /// <param name="collection">The collection whose elements are copied to the new list.</param>
    public ConsoleHelpTextCollectionModel(IEnumerable<ConsoleHelpTextModel> collection)
        : base(collection) { }

    /// <summary>
    ///     This method writes a 120-column banner with <paramref name="header" /> to the provided
    ///     <paramref name="builder" />.
    /// </summary>
    /// <param name="builder">The <see cref="StringBuilder" /> to write the <paramref name="header" /> block to.</param>
    /// <param name="header">The header text to write.</param>
    /// <returns>The updated <paramref name="builder" /> instance.</returns>
    private StringBuilder WriteHeader(StringBuilder builder, string header) =>
        builder
            .AppendLine()
            .AppendLine(string.Join("", Enumerable.Repeat("=", 120)))
            .AppendLine(
                $"=== {header} {string.Join("", Enumerable.Repeat("=", 120 - header.Length - 5))}"
            )
            .AppendLine(string.Join("", Enumerable.Repeat("=", 120)))
            .AppendLine()
            .AppendLine();

    /// <summary>
    ///     This method converts the instance into its string representation which is a help text block.
    /// </summary>
    /// <returns>The string representation of the instance.</returns>
    public override string ToString() => ToStringBuilder().ToString();

    /// <summary>
    ///     This method converts the instance into its <see cref="StringBuilder" /> representation.
    /// </summary>
    /// <param name="builder">Optional <see cref="StringBuilder" /> to write the help text to.</param>
    /// <param name="skipInformationBlock">If true, the information block will not be written.</param>
    /// <returns>The <see cref="StringBuilder" /> representation of the instance.</returns>
    public StringBuilder ToStringBuilder(
        StringBuilder builder = null,
        bool skipInformationBlock = false
    )
    {
        // Start with the information block, unless it is skipped.
        builder = WriteStartHelpText(builder, skipInformationBlock);

        // Localize the verbs: the default verb first, then the others by name.
        List<ConsoleHelpTextModel> verbs =
        [
            .. this.Where(e => e.Verb).Where(e => e.IsDefault).Take(1),
            .. this.Where(e => e.Verb)
                .Where(e => !e.IsDefault)
                .OrderBy(e => e.Name)
                .DistinctBy(v => v.Name),
        ];

        // Localize the positional arguments: required ones first, each in position order.
        List<ConsoleHelpTextModel> positionals =
        [
            .. OrderPositionals(this.Where(e => e.Required)),
            .. OrderPositionals(this.Where(e => !e.Required)),
        ];

        // Localize the named arguments: required ones first, each by short name or name.
        List<ConsoleHelpTextModel> named =
        [
            .. OrderNamed(this.Where(e => e.Required)),
            .. OrderNamed(this.Where(e => !e.Required)),
        ];

        // Write each section; a section without entries is skipped.
        WriteSection(builder, "Verb", verbs);
        WriteSection(builder, "Positional Arguments", positionals);
        WriteSection(builder, "Named Arguments", named);

        // We're done, return the updated builder.
        return builder.AppendLine();
    }

    /// <summary>
    ///     This method keeps the positional arguments among <paramref name="entries" />, grouped
    ///     and ordered by position, with one entry per position.
    /// </summary>
    /// <param name="entries">The entries to filter.</param>
    /// <returns>The ordered positional entries.</returns>
    private static IEnumerable<ConsoleHelpTextModel> OrderPositionals(
        IEnumerable<ConsoleHelpTextModel> entries
    ) =>
        entries
            .Where(e => e.Position >= 0)
            .Where(e => !e.Verb)
            .GroupBy(e => e.Group)
            .SelectMany(g => g.OrderBy(e => e.Position))
            .DistinctBy(v => v.Position);

    /// <summary>
    ///     This method keeps the named arguments among <paramref name="entries" />, grouped and
    ///     ordered by short name or name, with one entry per name.
    /// </summary>
    /// <param name="entries">The entries to filter.</param>
    /// <returns>The ordered named entries.</returns>
    private static IEnumerable<ConsoleHelpTextModel> OrderNamed(
        IEnumerable<ConsoleHelpTextModel> entries
    ) =>
        entries
            .Where(e => e.Position < 0)
            .Where(e => !e.Verb)
            .GroupBy(e => e.Group)
            .SelectMany(g => g.OrderBy(e => e.ShortName?.ToString() ?? e.Name))
            .DistinctBy(v => v.Name);

    /// <summary>
    ///     This method writes one help section: a banner and one entry per line. The name column is
    ///     as wide as the longest rendered name of this section (<c>-x, --name</c>, <c>[--name]</c>,
    ///     <c>&lt;0&gt;</c>), so the descriptions line up under each other whatever the names look
    ///     like, and continuation lines of a multi-line description start at the description column.
    ///     Each section measures only its own entries.
    /// </summary>
    /// <param name="builder">The <see cref="StringBuilder" /> to write to.</param>
    /// <param name="header">The banner text of the section.</param>
    /// <param name="entries">The entries of the section, in output order. Nothing is written when empty.</param>
    private void WriteSection(
        StringBuilder builder,
        string header,
        IReadOnlyList<ConsoleHelpTextModel> entries
    )
    {
        // A section without entries has nothing to measure or show.
        if (entries.Count is 0)
            return;

        // Add the section banner.
        WriteHeader(builder, header);

        // The name column is as wide as the longest rendered name in this section.
        int width = entries.Max(e => e.GetHelpName().Length);

        // The description column starts after the indentation, the name column and the gap.
        int descriptionColumn = Tab.Length + width + Tab.Length;

        // Write each entry followed by a blank line.
        foreach (ConsoleHelpTextModel entry in entries)
        {
            Tuple<string, string> pair = entry.ToHelpStringPair(width, descriptionColumn);
            builder.AppendLine($"{Tab}{pair.Item1}{Tab}{pair.Item2}").AppendLine();
        }
    }

    /// <summary>
    ///     This method fluently resets the <paramref name="applicationName" /> for the collection.
    /// </summary>
    /// <param name="applicationName">The name of the application.</param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithApplication(string applicationName)
    {
        // Reset the application name into the collection.
        Application = applicationName?.Trim();

        // We're done, return the updated collection.
        return this;
    }

    /// <summary>
    ///     This method fluently resets the <paramref name="author" /> for the collection.
    /// </summary>
    /// <param name="author">The author information for the application.</param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithAuthor(string author)
    {
        // Reset the author into the collection.
        Author = author?.Trim();

        // We're done, return the updated collection.
        return this;
    }

    /// <summary>
    ///     This method fluently resets the <paramref name="company" /> for the collection.
    /// </summary>
    /// <param name="company">The company information for the application.</param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithCompany(string company)
    {
        // Reset the company into the collection.
        Company = company?.Trim();

        // We're done, return the updated collection.
        return this;
    }

    /// <summary>
    ///     This method fluently resets the <paramref name="description" /> for the collection.
    /// </summary>
    /// <param name="description">The description information for the application.</param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithDescription(string description)
    {
        // Reset the description into the collection.
        Description = description?.Trim();

        // We're done, return the updated collection.
        return this;
    }

    /// <summary>
    ///     This method fluently adds a new <paramref name="element" /> to the collection.
    /// </summary>
    /// <param name="element">The <see cref="ConsoleHelpTextModel" /> element to add to the collection.</param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithElement(ConsoleHelpTextModel element)
    {
        // Add the element to the collection.
        Add(element);

        // We're done, return the updated collection.
        return this;
    }

    /// <summary>
    ///     This method fluently adds a new element to the collection using the
    ///     provided <paramref name="action" /> to configure the new element.
    /// </summary>
    /// <param name="action">The <see cref="Action{ConsoleHelpTextModel}" /> to configure the new element.</param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithElement(Action<ConsoleHelpTextModel> action)
    {
        // Instantiate our new element.
        ConsoleHelpTextModel element = new();

        // Invoke the action to configure the element.
        action?.Invoke(element);

        // We're done, add the element to the collection then return the updated collection.
        return WithElement(element);
    }

    /// <summary>
    ///     This method fluently adds a range of new <paramref name="elements" /> to the collection.
    /// </summary>
    /// <param name="elements">
    ///     The collection of <see cref="ConsoleHelpTextModel" /> elements to add to the collection.
    /// </param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithElements(IEnumerable<ConsoleHelpTextModel> elements)
    {
        // Add the elements to the collection.
        AddRange(elements ?? []);

        // We're done, return the updated collection.
        return this;
    }

    /// <summary>
    ///     This method fluently adds a range of new <paramref name="elements" /> to the collection.
    /// </summary>
    /// <param name="elements">
    ///     The array of <see cref="ConsoleHelpTextModel" /> elements to add to the collection.
    /// </param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithElements(params ConsoleHelpTextModel[] elements) =>
        WithElements((IEnumerable<ConsoleHelpTextModel>)elements);

    /// <summary>
    ///     This method fluently resets the <paramref name="version" /> for the collection.
    /// </summary>
    /// <param name="version">The version information for the application.</param>
    /// <returns>The updated <see cref="ConsoleHelpTextCollectionModel" /> collection.</returns>
    public ConsoleHelpTextCollectionModel WithVersion(string version)
    {
        // Reset the version into the collection.
        Version = version?.Trim();

        // We're done, return the updated collection.
        return this;
    }

    /// <summary>
    ///     This method writes the application information block (name, version, copyright line and
    ///     description) to the provided <paramref name="builder" />, when any of those fields are set.
    ///     The copyright line uses the current year.
    /// </summary>
    /// <param name="builder">Optional <see cref="StringBuilder" /> to write the information block to.</param>
    /// <param name="skipInformationBlock">If true, the information block will not be written.</param>
    /// <returns>The updated <paramref name="builder" /> instance.</returns>
    public StringBuilder WriteStartHelpText(
        StringBuilder builder = null,
        bool skipInformationBlock = false
    )
    {
        // Ensure we have a builder to work with.
        builder ??= new StringBuilder().AppendLine();

        // Skip the block when asked to or when every metadata field is empty.
        if (
            skipInformationBlock
            || Enumerable.All(
                [Application, Author, Company, Description, Version],
                v => v is null or ""
            )
        )
            return builder;

        // Append our information header.
        WriteHeader(builder, "Application Information");

        // Check for an application name.
        if (Application is not (null or ""))
            builder.AppendLine("Name:").AppendLine($"{Tab}{Application}").AppendLine();

        // Check for a version.
        if (Version is not (null or ""))
            builder.AppendLine("Version:").AppendLine($"{Tab}{Version}").AppendLine();

        // Check for an author and a company then add the line to the help text.
        if (Author is not (null or "") && Company is not (null or ""))
            builder
                .AppendLine("Copyright:")
                .AppendLine($"{Tab}{DateTimeOffset.Now.Year} {Company} [{Author}]")
                .AppendLine();
        // Check for just an author then add the line to the help text.
        else if (Author is not (null or ""))
            builder
                .AppendLine("Copyright:")
                .AppendLine($"{Tab}{DateTimeOffset.Now.Year} {Author}")
                .AppendLine();
        // Check for just a company then add the line to the help text.
        else if (Company is not (null or ""))
            builder
                .AppendLine("Copyright:")
                .AppendLine($"{Tab}{DateTimeOffset.Now.Year} {Company}")
                .AppendLine();

        // Check for a description.
        if (Description is not (null or ""))
            builder
                .AppendLine("Description:")
                .AppendLine(
                    string.Join(
                        Environment.NewLine,
                        Description.Split(Environment.NewLine).Select(l => $"{Tab}{l}")
                    )
                )
                .AppendLine();

        // We're done, return the updated builder.
        return builder;
    }
}
