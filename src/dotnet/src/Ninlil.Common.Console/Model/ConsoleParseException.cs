// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This exception is what <c>Parse</c> throws when a command line does not produce a model: help
///     was requested, the model's verb did not match, or the arguments are invalid. It carries the
///     <see cref="ConsoleParseResult" /> so a handler can show the help text and choose an exit code.
///     It derives from <see cref="InvalidOperationException" /> because <c>Parse</c> threw that type
///     for a mismatched verb before parse results existed, so existing
///     <c>catch (InvalidOperationException)</c> blocks and not-parsed handlers registered for it keep
///     working.
/// </summary>
public class ConsoleParseException : InvalidOperationException
{
    /// <summary>
    ///     This constructor creates the exception for a result.
    /// </summary>
    /// <param name="result">The result being reported.</param>
    /// <param name="results">
    ///     Every result tried, in order. When null, only <paramref name="result" /> is listed.
    /// </param>
    /// <exception cref="ArgumentNullException">When <paramref name="result" /> is null.</exception>
    public ConsoleParseException(
        ConsoleParseResult result,
        IEnumerable<ConsoleParseResult> results = null
    )
        : base(BuildMessage(result, results), result?.Exception)
    {
        Result = result;
        Results = (results ?? [result]).ToList().AsReadOnly();
    }

    /// <summary>
    ///     This property contains the result being reported.
    /// </summary>
    public ConsoleParseResult Result { get; }

    /// <summary>
    ///     This property contains every result tried, in order. It has more than one entry when
    ///     several input models were tried.
    /// </summary>
    public IReadOnlyList<ConsoleParseResult> Results { get; }

    /// <summary>
    ///     This method builds the message: the result's help text (which already starts with its
    ///     errors) or its errors alone, or a "No input model matched" line when every result is
    ///     <see cref="Enum.ConsoleParseOutcomeEnum.NotMatched" />.
    /// </summary>
    /// <param name="result">The result being reported.</param>
    /// <param name="results">Every result tried, or null.</param>
    /// <returns>The exception message.</returns>
    private static string BuildMessage(
        ConsoleParseResult result,
        IEnumerable<ConsoleParseResult> results
    )
    {
        ArgumentNullException.ThrowIfNull(result);

        // When no model's verb fitted, name that instead of one model's mismatch.
        if ((results ?? [result]).All(r => r.Outcome is Enum.ConsoleParseOutcomeEnum.NotMatched))
            return $"No input model matched the arguments. {string.Join(" ", result.Errors)}".Trim();

        // The help text of an invalid result begins with the errors, so printing both would repeat them.
        return string.IsNullOrWhiteSpace(result.HelpText)
            ? string.Join(Environment.NewLine, result.Errors)
            : result.HelpText;
    }
}
