using System.Text.RegularExpressions;
using Ninlil.Common.Configuration;
using Ninlil.Web.EnvironmentVariables;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that <c>ENVIRONMENT.md</c>, the registry of every <c>NINLIL_*</c>
///     variable, matches the code, in both directions: each variable the registry maps
///     (<see cref="OptionsEnvironmentVariableMap.BuildMappings" /> over
///     <see cref="OptionsEnvironmentVariableRegistry.Roots" /> and
///     <see cref="OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths" />) has a table
///     row, each row names a variable that exists, the framework-level table matches
///     <see cref="FrameworkEnvironmentVariableBootstrap.RecognizedVariableNames" />, and the totals
///     in the document's opening paragraph match the counts. The <c>NINLIL_DOTNET_*</c> rows in
///     the ".NET runtime settings" section are recognized names that the document says are not
///     part of the total, so they are not compared. The test reads the file from the repository
///     root (found by walking up from the test binaries to <c>Ninlil.slnx</c>) and needs no host.
///     Two more tests feed the comparison a copy of the document with a row removed and another with a
///     wrong total, to prove the comparison reports both.
/// </summary>
public partial class EnvironmentVariableDocumentationTests
{
    /// <summary>
    ///     This constant contains the heading that starts the framework-level table, which the
    ///     comparison reads separately from the settings tables.
    /// </summary>
    private const string FrameworkHeading = "## Framework-level variables";

    /// <summary>
    ///     This constant contains the heading that ends the framework-level table and starts the
    ///     <c>NINLIL_DOTNET_*</c> table the comparison skips.
    /// </summary>
    private const string RuntimeSettingsHeading = "### .NET runtime settings";

    /// <summary>
    ///     This constant contains the heading that ends the skipped <c>NINLIL_DOTNET_*</c> table;
    ///     the standalone variables follow it.
    /// </summary>
    private const string BootstrapHeading = "## Bootstrap administrator credentials";

    [Fact]
    public void EnvironmentMd_ListsEveryRegisteredVariableAndNothingElse_WithMatchingTotals()
    {
        string markdown = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "ENVIRONMENT.md"));

        List<string> problems = FindDrift(markdown);

        Assert.True(
            problems.Count == 0,
            "ENVIRONMENT.md does not match the registry:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, problems)
        );
    }

    [Fact]
    public void FindDrift_WhenARowIsMissing_ReportsTheVariable()
    {
        string markdown = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "ENVIRONMENT.md"));
        const string removed = "NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_PORT";
        string withoutRow = string.Join(
            "\n",
            markdown
                .Split('\n')
                .Where(line => !line.StartsWith($"| `{removed}` |", StringComparison.Ordinal))
        );
        Assert.NotEqual(markdown, withoutRow);

        List<string> problems = FindDrift(withoutRow);

        Assert.Contains(problems, problem => problem.Contains(removed));
    }

    [Fact]
    public void FindDrift_WhenTheSettingsTotalIsWrong_ReportsTheTotal()
    {
        string markdown = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "ENVIRONMENT.md"));
        string wrongTotal = Regex.Replace(
            markdown,
            @"\*\*\d+ individual settings\*\*",
            "**1 individual settings**"
        );
        Assert.NotEqual(markdown, wrongTotal);

        List<string> problems = FindDrift(wrongTotal);

        Assert.Contains(problems, problem => problem.Contains("1 individual settings"));
    }

    /// <summary>
    ///     This method compares the document text with the registry and returns one line per
    ///     difference: a registered variable with no row, a row with no registered variable, a
    ///     variable listed twice, a framework-level mismatch or a total that is wrong. An empty
    ///     list means the document is correct.
    /// </summary>
    /// <param name="markdown">The text of <c>ENVIRONMENT.md</c>.</param>
    private static List<string> FindDrift(string markdown)
    {
        List<string> problems = [];

        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings(
            OptionsEnvironmentVariableRegistry.Roots,
            OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths
        );
        int standaloneCount = OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths.Count;
        int frameworkCount = FrameworkEnvironmentVariableBootstrap.RecognizedVariableNames.Count;

        int frameworkStart = markdown.IndexOf(FrameworkHeading, StringComparison.Ordinal);
        int runtimeStart = markdown.IndexOf(RuntimeSettingsHeading, StringComparison.Ordinal);
        int bootstrapStart = markdown.IndexOf(BootstrapHeading, StringComparison.Ordinal);
        Assert.True(
            0 < frameworkStart && frameworkStart < runtimeStart && runtimeStart < bootstrapStart,
            "ENVIRONMENT.md lost one of the headings this test splits it on."
        );

        // Settings tables (everything before the framework section) and the standalone tables
        // (after the skipped NINLIL_DOTNET_* table) both hold names the registry maps.
        List<string> documentedMapped = RowNames(markdown[..frameworkStart])
            .Concat(RowNames(markdown[bootstrapStart..]))
            .ToList();
        List<string> documentedFramework = RowNames(markdown[frameworkStart..runtimeStart])
            .ToList();

        problems.AddRange(
            documentedMapped
                .GroupBy(name => name)
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key} has more than one row.")
        );
        problems.AddRange(
            mappings
                .Keys.Except(documentedMapped)
                .Order(StringComparer.Ordinal)
                .Select(name => $"{name} is registered but has no row.")
        );
        problems.AddRange(
            documentedMapped
                .Except(mappings.Keys)
                .Order(StringComparer.Ordinal)
                .Select(name => $"{name} has a row but is not registered.")
        );
        problems.AddRange(
            FrameworkEnvironmentVariableBootstrap
                .RecognizedVariableNames.Except(documentedFramework)
                .Order(StringComparer.Ordinal)
                .Select(name => $"{name} is a framework-level variable but has no row.")
        );
        problems.AddRange(
            documentedFramework
                .Except(FrameworkEnvironmentVariableBootstrap.RecognizedVariableNames)
                .Order(StringComparer.Ordinal)
                .Select(name => $"{name} has a framework-level row but is not recognized.")
        );

        // The totals paragraph wraps lines, so whitespace is collapsed before matching.
        string opening = Regex.Replace(
            markdown[..markdown.IndexOf("---", StringComparison.Ordinal)],
            @"\s+",
            " "
        );
        Match totals = TotalsPattern().Match(opening);

        if (!totals.Success)
        {
            problems.Add("The 'Total: ...' paragraph no longer has the expected wording.");
            return problems;
        }

        CheckTotal(
            problems,
            "individual settings",
            totals,
            "settings",
            mappings.Count - standaloneCount
        );
        CheckTotal(
            problems,
            "bound configuration classes",
            totals,
            "classes",
            OptionsEnvironmentVariableRegistry.Roots.Count
        );
        CheckTotal(problems, "standalone variables", totals, "standalone", standaloneCount);
        CheckTotal(problems, "framework-level variables", totals, "framework", frameworkCount);
        CheckTotal(
            problems,
            "environment variables in total",
            totals,
            "total",
            mappings.Count + frameworkCount
        );

        return problems;
    }

    /// <summary>
    ///     This method adds a problem line when a number in the totals paragraph differs from the
    ///     count taken from the code.
    /// </summary>
    private static void CheckTotal(
        List<string> problems,
        string label,
        Match totals,
        string groupName,
        int expected
    )
    {
        int documented = int.Parse(totals.Groups[groupName].Value);

        if (documented != expected)
            problems.Add($"The document states {documented} {label}; the registry has {expected}.");
    }

    /// <summary>
    ///     This method returns the <c>NINLIL_*</c> name in the first column of each table row of
    ///     the text. Prose and the "Also recognized" lists are not table rows and are ignored.
    /// </summary>
    private static IEnumerable<string> RowNames(string text) =>
        RowPattern().Matches(text).Select(match => match.Groups[1].Value);

    /// <summary>
    ///     This method finds the repository root by walking up from the test binaries to the
    ///     directory that holds <c>Ninlil.slnx</c>.
    /// </summary>
    private static string FindRepositoryRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);

        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ninlil.slnx"))
        )
            directory = directory.Parent;

        Assert.True(directory is not null, "Ninlil.slnx was not found above the test binaries.");
        return directory.FullName;
    }

    /// <summary>
    ///     This pattern matches a table row whose first cell is one backticked <c>NINLIL_*</c>
    ///     name and captures the name.
    /// </summary>
    [GeneratedRegex(@"^\| `(NINLIL_[A-Z0-9_]+)` \|", RegexOptions.Multiline)]
    private static partial Regex RowPattern();

    /// <summary>
    ///     This pattern matches the document's totals sentence and captures its five numbers.
    /// </summary>
    [GeneratedRegex(
        @"Total: \*\*(?<settings>\d+) individual settings\*\* across (?<classes>\d+) bound configuration classes, plus \*\*(?<standalone>\d+) standalone\*\* .*? plus \*\*(?<framework>\d+) framework-level\*\* .*? \*\*(?<total>\d+) environment variables in total\*\*"
    )]
    private static partial Regex TotalsPattern();
}
