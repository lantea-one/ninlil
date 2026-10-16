using Ninlil.Common.Console;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     These tests cover <see cref="ConsoleHelpTextCollectionModel" />: the fluent <c>With*</c>
///     builders, the application information block, and the help text rendered from the test input
///     models in <c>TestSupport</c>.
/// </summary>
public class ConsoleHelpTextCollectionModelTests
{
    /// <summary>Verifies that <see cref="ConsoleHelpTextCollectionModel.Tab" /> is four spaces.</summary>
    [Fact]
    public void Tab_IsFourSpaces()
    {
        Assert.Equal("    ", ConsoleHelpTextCollectionModel.Tab);
    }

    /// <summary>Verifies that the With* fluent setters trim and store their values.</summary>
    [Fact]
    public void WithApplicationAuthorCompanyDescriptionVersion_TrimAndStore()
    {
        ConsoleHelpTextCollectionModel collection = new ConsoleHelpTextCollectionModel()
            .WithApplication("  MyApp  ")
            .WithAuthor("  Jane Doe  ")
            .WithCompany("  Acme  ")
            .WithDescription("  Does things.  ")
            .WithVersion("  1.0.0  ");

        Assert.Equal("MyApp", collection.Application);
        Assert.Equal("Jane Doe", collection.Author);
        Assert.Equal("Acme", collection.Company);
        Assert.Equal("Does things.", collection.Description);
        Assert.Equal("1.0.0", collection.Version);
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextCollectionModel.WithElement(ConsoleHelpTextModel)" /> appends a single element.</summary>
    [Fact]
    public void WithElement_Single_AppendsElement()
    {
        ConsoleHelpTextCollectionModel collection =
            new ConsoleHelpTextCollectionModel().WithElement(
                new ConsoleHelpTextModel { Name = "name" }
            );

        Assert.Single(collection);
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextCollectionModel.WithElement(Action{ConsoleHelpTextModel})" /> configures and appends a new element.</summary>
    [Fact]
    public void WithElement_Action_ConfiguresAndAppendsElement()
    {
        ConsoleHelpTextCollectionModel collection =
            new ConsoleHelpTextCollectionModel().WithElement(e => e.Name = "configured");

        Assert.Single(collection);
        Assert.Equal("configured", collection[0].Name);
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextCollectionModel.WithElements(IEnumerable{ConsoleHelpTextModel})" /> appends every element.</summary>
    [Fact]
    public void WithElementsEnumerable_AppendsEveryElement()
    {
        ConsoleHelpTextCollectionModel collection =
            new ConsoleHelpTextCollectionModel().WithElements([
                new ConsoleHelpTextModel { Name = "a" },
                new ConsoleHelpTextModel { Name = "b" },
            ]);

        Assert.Equal(2, collection.Count);
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextCollectionModel.WithElements(ConsoleHelpTextModel[])" /> appends every element.</summary>
    [Fact]
    public void WithElementsParams_AppendsEveryElement()
    {
        ConsoleHelpTextCollectionModel collection =
            new ConsoleHelpTextCollectionModel().WithElements(
                new ConsoleHelpTextModel { Name = "a" },
                new ConsoleHelpTextModel { Name = "b" }
            );

        Assert.Equal(2, collection.Count);
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextCollectionModel.WriteStartHelpText" /> includes the application information block when metadata is present.</summary>
    [Fact]
    public void WriteStartHelpText_WithMetadata_IncludesInformationBlock()
    {
        ConsoleHelpTextCollectionModel collection = new ConsoleHelpTextCollectionModel()
            .WithApplication("MyApp")
            .WithVersion("1.0.0");

        string result = collection.WriteStartHelpText().ToString();

        Assert.Contains("Application Information", result);
        Assert.Contains("MyApp", result);
        Assert.Contains("1.0.0", result);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleHelpTextCollectionModel.WriteStartHelpText" /> omits the
    ///     information block when no metadata is present. The metadata properties are cleared
    ///     explicitly because a new instance copies them from <see cref="ConsoleCommand" />'s static
    ///     fields, which hold the test host's assembly details and may have been changed by other
    ///     tests.
    /// </summary>
    [Fact]
    public void WriteStartHelpText_WithoutMetadata_OmitsInformationBlock()
    {
        ConsoleHelpTextCollectionModel collection = new()
        {
            Application = null,
            Author = null,
            Company = null,
            Description = null,
            Version = null,
        };

        string result = collection.WriteStartHelpText().ToString();

        Assert.DoesNotContain("Application Information", result);
    }

    /// <summary>Verifies that <see cref="ConsoleHelpTextCollectionModel.WriteStartHelpText" /> respects the skip-information-block flag even when metadata is present.</summary>
    [Fact]
    public void WriteStartHelpText_WithSkipFlag_OmitsInformationBlockEvenWithMetadata()
    {
        ConsoleHelpTextCollectionModel collection =
            new ConsoleHelpTextCollectionModel().WithApplication("MyApp");

        string result = collection.WriteStartHelpText(null, true).ToString();

        Assert.DoesNotContain("Application Information", result);
    }

    /// <summary>Verifies that the copyright line combines both author and company when both are present.</summary>
    [Fact]
    public void WriteStartHelpText_WithAuthorAndCompany_CombinesInCopyrightLine()
    {
        ConsoleHelpTextCollectionModel collection = new ConsoleHelpTextCollectionModel()
            .WithAuthor("Jane Doe")
            .WithCompany("Acme");

        string result = collection.WriteStartHelpText().ToString();

        Assert.Contains("Copyright:", result);
        Assert.Contains("Acme", result);
        Assert.Contains("Jane Doe", result);
    }

    /// <summary>
    ///     Verifies that the help text built from <see cref="VerbModel" />'s attributes has a verb
    ///     section and a named-arguments section.
    /// </summary>
    [Fact]
    public void ToString_ForRealisticModel_RendersAllSections()
    {
        ConsoleHelpTextCollectionModel collection = ConsoleCommandInputModel.GetHelpTextCollection(
            typeof(VerbModel)
        );

        string result = collection.ToString();

        Assert.Contains("Verb", result);
        Assert.Contains("add", result);
        Assert.Contains("Named Arguments", result);
        Assert.Contains("name", result);
    }

    /// <summary>Verifies that positional arguments render under a separate header from named arguments.</summary>
    [Fact]
    public void ToString_WithPositionalArguments_RendersPositionalArgumentsHeader()
    {
        ConsoleHelpTextCollectionModel collection = ConsoleCommandInputModel.GetHelpTextCollection(
            typeof(PositionalArgsModel)
        );

        string result = collection.ToString();

        Assert.Contains("Positional Arguments", result);
    }

    /// <summary>
    ///     This method finds the column where each entry's bracketed description line starts in
    ///     the rendered help text.
    /// </summary>
    /// <param name="text">The rendered help text.</param>
    /// <returns>The column of the <c>[Required:</c> marker on every entry line.</returns>
    private static List<int> DescriptionColumns(string text) =>
        text.Split(Environment.NewLine)
            .Select(l => l.IndexOf("[Required:", StringComparison.Ordinal))
            .Where(i => i >= 0)
            .ToList();

    /// <summary>Verifies that the descriptions of the named section start at one column although the names differ in form and length.</summary>
    [Fact]
    public void ToString_ForNamedArguments_AlignsTheDescriptionColumn()
    {
        string result = ConsoleCommandInputModel
            .GetHelpTextCollection(typeof(SimpleNamedArgsModel))
            .ToString();

        List<int> columns = DescriptionColumns(result);

        Assert.True(columns.Count > 1);
        Assert.Single(columns.Distinct());
    }

    /// <summary>Verifies that the positional section is measured from its own entries, and its descriptions line up.</summary>
    [Fact]
    public void ToString_ForPositionalArguments_AlignsTheDescriptionColumn()
    {
        string result = ConsoleCommandInputModel
            .GetHelpTextCollection(typeof(PositionalArgsModel))
            .ToString();

        string positional = result[
            result.IndexOf("Positional Arguments", StringComparison.Ordinal)..
        ];
        positional = positional[..positional.IndexOf("Named Arguments", StringComparison.Ordinal)];
        List<int> columns = DescriptionColumns(positional);

        Assert.Equal(2, columns.Count);
        Assert.Single(columns.Distinct());
    }

    /// <summary>Verifies that a collection with only positional entries renders its section (no named entries to measure).</summary>
    [Fact]
    public void ToString_WithOnlyPositionalEntries_RendersThePositionalSection()
    {
        ConsoleHelpTextCollectionModel collection = new ConsoleHelpTextCollectionModel()
            .WithApplication(null)
            .WithAuthor(null)
            .WithCompany(null)
            .WithDescription(null)
            .WithVersion(null)
            .WithElement(e =>
            {
                e.Position = 0;
                e.Name = "source";
                e.Required = true;
                e.Description = "The source.";
            });

        string result = collection.ToString();

        Assert.Contains("Positional Arguments", result);
        Assert.Contains("<0>", result);
        Assert.DoesNotContain("Named Arguments", result);
        Assert.DoesNotContain("Verb", result);
    }

    /// <summary>Verifies that continuation lines of a multi-line description start at the description column.</summary>
    [Fact]
    public void ToString_WithMultiLineDescription_AlignsContinuationLinesToTheDescriptionColumn()
    {
        string continuation = new('z', 40);
        ConsoleHelpTextCollectionModel collection = new ConsoleHelpTextCollectionModel()
            .WithApplication(null)
            .WithAuthor(null)
            .WithCompany(null)
            .WithDescription(null)
            .WithVersion(null)
            .WithElement(e =>
            {
                e.Name = "first";
                e.ShortName = 'f';
                e.Required = true;
                e.Description = $"{new string('y', 40)}{Environment.NewLine}{continuation}";
            })
            .WithElement(e =>
            {
                e.Name = "second";
                e.Required = false;
                e.Description = new string('x', 40);
            });

        string[] lines = collection.ToString().Split(Environment.NewLine);
        int column = DescriptionColumns(string.Join(Environment.NewLine, lines))[0];
        string line = lines.Single(l => l.EndsWith(continuation, StringComparison.Ordinal));

        Assert.Equal(column, line.Length - continuation.Length);
        Assert.True(string.IsNullOrWhiteSpace(line[..column]));
    }

    /// <summary>Verifies that short names and brackets widen the name column instead of pushing one description out of line.</summary>
    [Fact]
    public void ToString_WithShortNamesAndBrackets_AlignsTheDescriptionColumn()
    {
        ConsoleHelpTextCollectionModel collection = new ConsoleHelpTextCollectionModel()
            .WithApplication(null)
            .WithAuthor(null)
            .WithCompany(null)
            .WithDescription(null)
            .WithVersion(null)
            .WithElement(e =>
            {
                e.Name = "ab";
                e.ShortName = 'x';
                e.Required = true;
                e.Description = "Short name.";
            })
            .WithElement(e =>
            {
                e.Name = "cde";
                e.Required = false;
                e.Description = "Long name.";
            })
            .WithElement(e =>
            {
                e.Name = "gh";
                e.Required = false;
                e.Description = "Optional.";
            });

        List<int> columns = DescriptionColumns(collection.ToString());

        Assert.Equal(3, columns.Count);
        Assert.Single(columns.Distinct());
    }
}
