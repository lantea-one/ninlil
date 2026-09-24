using Ninlil.Common.Console;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace for the console-help-text-collection-model tests.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     This test class exercises <see cref="ConsoleHelpTextCollectionModel" /> - the fluent
///     With*/WithElement(s) builders, the application-information header block, and the full
///     rendered help text produced for a realistic model mixing verbs, positional arguments, and
///     named arguments.
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
    ///     information block when no metadata is present. The collection's Application/Author/Company/
    ///     Description/Version properties are explicitly cleared here rather than relying on a
    ///     freshly constructed instance, since the constructor seeds them from
    ///     <see cref="ConsoleCommand" />'s own static, process-wide metadata fields (which pick up
    ///     ambient state such as the test host's own assembly name, and can also have been mutated by
    ///     other tests in the same run).
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
    ///     Verifies a full, real-world rendering: a model's help text collection is built from its own
    ///     attributes and produces sections for verbs, positional arguments, and named arguments.
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

    /// <summary>Verifies that positional arguments render under their own header, distinct from named arguments.</summary>
    [Fact]
    public void ToString_WithPositionalArguments_RendersPositionalArgumentsHeader()
    {
        ConsoleHelpTextCollectionModel collection = ConsoleCommandInputModel.GetHelpTextCollection(
            typeof(PositionalArgsModel)
        );

        string result = collection.ToString();

        Assert.Contains("Positional Arguments", result);
    }
}
