using System.Globalization;
using System.Reflection;
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

    /// <summary>
    ///     This dictionary lists the variables whose Default cell is deliberately not the plain
    ///     value of the property in a new instance of its options class, with the reason for
    ///     each. A variable only belongs here when the document cannot state the code value; a
    ///     plain mismatch is fixed in the document instead.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultAllowList = new(
        StringComparer.Ordinal
    )
    {
        ["NINLIL_IDENTITY_PASSWORD_REQUIRED_LENGTH"] =
            "ASP.NET Core's IdentityOptions starts at 6; AddNinlilIdentity sets 12 before the configuration is read, and the row states 12.",
        ["NINLIL_IDENTITY_USER_REQUIRE_UNIQUE_EMAIL"] =
            "ASP.NET Core's IdentityOptions starts at false; AddNinlilIdentity sets true before the configuration is read, and the row states true.",
        ["NINLIL_IDENTITY_LOCKOUT_DEFAULT_LOCKOUT_TIME_SPAN"] =
            "ASP.NET Core's IdentityOptions starts at 5 minutes; AddNinlilIdentity sets 15 minutes before the configuration is read, and the row states 00:15:00.",
        ["NINLIL_DNS_SERVER_AUTHORITATIVE_HOSTNAME_STRING"] =
            "The default is Environment.MachineName, which differs per host; the row describes it in italics.",
        ["NINLIL_DNS_SERVER_PIPELINE_WORKER_COUNT"] =
            "The default is Environment.ProcessorCount, which differs per host; the row describes it in italics.",
    };

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

    [Fact]
    public void FindDrift_WhenADefaultIsWrong_ReportsTheVariable()
    {
        string markdown = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "ENVIRONMENT.md"));
        const string changed = "NINLIL_DNS_SERVER_WEB_HTTP_PORT";
        string wrongDefault = string.Join(
            "\n",
            markdown
                .Split('\n')
                .Select(line =>
                    line.StartsWith($"| `{changed}` |", StringComparison.Ordinal)
                        ? line[..line.LastIndexOf("| `80`", StringComparison.Ordinal)]
                            + "| `8080` |"
                        : line
                )
        );
        Assert.NotEqual(markdown, wrongDefault);

        List<string> problems = FindDrift(wrongDefault);

        Assert.Contains(problems, problem => problem.Contains(changed) && problem.Contains("8080"));
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

        CheckDefaults(problems, markdown, mappings);

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
    ///     This method compares the Default cell of every row of a mapped variable with the
    ///     default the code gives the property. A property with a plain default value (a number,
    ///     a boolean, an enum, a <see cref="TimeSpan" />, a <see cref="Guid" /> or a non-empty
    ///     string) must have a row whose first backticked token equals that value (booleans
    ///     ignore case); text after the token, such as a note in parentheses, is ignored. A
    ///     property whose default is an empty string or no value must have a row that starts with
    ///     <c>*(empty</c> or <c>*(none</c>. A file default is an absolute path under the base
    ///     directory and is compared relative to it, without a trailing slash. A nested options
    ///     class (for example <c>StorageOptions.MigrationSafety</c>) is compared through the
    ///     instance its parent creates, because the binder starts from that instance; a nested
    ///     property whose parent leaves it <see langword="null" /> has no default and is skipped.
    ///     Variables in <see cref="DefaultAllowList" /> and standalone variables (no options
    ///     class) are skipped.
    /// </summary>
    /// <param name="problems">The list the differences are added to.</param>
    /// <param name="markdown">The text of <c>ENVIRONMENT.md</c>.</param>
    /// <param name="mappings">The registry's variable name to configuration path table.</param>
    private static void CheckDefaults(
        List<string> problems,
        string markdown,
        IReadOnlyDictionary<string, string> mappings
    )
    {
        Dictionary<string, string> cells = DefaultCells(markdown);

        foreach (
            (string name, string path) in mappings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
        )
        {
            if (DefaultAllowList.ContainsKey(name) || !cells.TryGetValue(name, out string cell))
                continue;

            if (!ReadDefault(path, out object value))
                continue;

            string expected = value switch
            {
                null => null,
                bool flag => flag ? "true" : "false",
                TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
                Enum member => member.ToString(),
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString(),
            };
            bool isFilePath =
                expected is not null
                && expected.StartsWith(AppContext.BaseDirectory, StringComparison.Ordinal);

            if (isFilePath)
                expected = expected[AppContext.BaseDirectory.Length..]
                    .Replace('\\', '/')
                    .TrimEnd('/');

            if (string.IsNullOrEmpty(expected))
            {
                if (
                    !cell.StartsWith("*(empty", StringComparison.Ordinal)
                    && !cell.StartsWith("*(none", StringComparison.Ordinal)
                )
                    problems.Add($"{name} has no value by default but its Default cell is: {cell}");

                continue;
            }

            Match token = FirstTokenPattern().Match(cell);
            string documented = token.Success ? token.Groups[1].Value : null;

            if (isFilePath && documented is not null)
                documented = documented.TrimEnd('/');

            if (
                !string.Equals(documented, expected, StringComparison.OrdinalIgnoreCase)
                || (value is not bool && documented != expected)
            )
                problems.Add(
                    $"{name} defaults to '{expected}' in code but its Default cell is: {cell}"
                );
        }
    }

    /// <summary>
    ///     This method reads the default of the property a configuration path names. It finds
    ///     the root whose section path is the longest prefix of the path, creates one instance of
    ///     the root class and follows the remaining segments through the nested instances.
    /// </summary>
    /// <param name="path">A configuration path such as <c>DnsServer:Web:HttpPort</c>.</param>
    /// <param name="value">The default, or <see langword="null" /> when it has no value.</param>
    /// <returns>
    ///     <see langword="false" /> when the path has no options class behind it (a standalone
    ///     path) or a nested instance on the way is <see langword="null" />.
    /// </returns>
    private static bool ReadDefault(string path, out object value)
    {
        value = null;
        OptionsEnvironmentVariableRoot best = default;
        string bestSection = null;

        foreach (OptionsEnvironmentVariableRoot root in OptionsEnvironmentVariableRegistry.Roots)
        {
            string section =
                root.SectionNameOverride
                ?? (string)root.OptionsType.GetField("SectionName").GetValue(null);

            if (
                path.StartsWith(section + ":", StringComparison.Ordinal)
                && (bestSection is null || section.Length > bestSection.Length)
            )
            {
                best = root;
                bestSection = section;
            }
        }

        if (bestSection is null)
            return false;

        object current = Activator.CreateInstance(best.OptionsType);

        foreach (string segment in path[(bestSection.Length + 1)..].Split(':'))
        {
            if (current is null)
                return false;

            PropertyInfo property = current
                .GetType()
                .GetProperty(segment, BindingFlags.Public | BindingFlags.Instance);

            // A standalone path can share a section prefix with a root (Identity:Bootstrap...)
            // without being a property of its class.
            if (property is null)
                return false;

            current = property.GetValue(current);
        }

        value = current;
        return true;
    }

    /// <summary>
    ///     This method returns the text of the last cell (the Default column) of each table row
    ///     in the document, keyed by the variable name in the first cell.
    /// </summary>
    private static Dictionary<string, string> DefaultCells(string markdown) =>
        DefaultCellPattern()
            .Matches(markdown)
            .ToDictionary(
                match => match.Groups[1].Value,
                match => match.Groups[2].Value.Trim(),
                StringComparer.Ordinal
            );

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
    ///     This pattern matches a table row of a mapped variable and captures the name and the
    ///     last cell, the Default column.
    /// </summary>
    [GeneratedRegex(@"^\| `(NINLIL_[A-Z0-9_]+)` \|.* \| ([^|]*) \|[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex DefaultCellPattern();

    /// <summary>
    ///     This pattern matches a Default cell that starts with a backticked value and captures
    ///     the value.
    /// </summary>
    [GeneratedRegex(@"^`([^`]+)`")]
    private static partial Regex FirstTokenPattern();

    /// <summary>
    ///     This pattern matches the document's totals sentence and captures its five numbers.
    /// </summary>
    [GeneratedRegex(
        @"Total: \*\*(?<settings>\d+) individual settings\*\* across (?<classes>\d+) bound configuration classes, plus \*\*(?<standalone>\d+) standalone\*\* .*? plus \*\*(?<framework>\d+) framework-level\*\* .*? \*\*(?<total>\d+) environment variables in total\*\*"
    )]
    private static partial Regex TotalsPattern();
}
