using Ninlil.Common.Console.Enum;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class is what parsing a command line into one input model reports. The parser never
///     prints and never ends the process: it returns this result and the host decides what to
///     show and which exit code to use.
///     <see cref="ConsoleCommandArgumentCollection.GetParseResult(System.Type)" /> and
///     <see cref="ConsoleCommandInputModel.GetParseResult(System.Type, string[])" /> create it;
///     <see cref="ConsoleParseException" /> carries it when <c>Parse</c> cannot return a model.
///     The instance is read-only after construction.
/// </summary>
public sealed class ConsoleParseResult
{
    /// <summary>
    ///     This constructor creates a result. Callers normally get one from
    ///     <see cref="ConsoleCommandArgumentCollection.GetParseResult(System.Type)" />.
    /// </summary>
    /// <param name="outcome">How the parse ended.</param>
    /// <param name="inputModelType">The input model type that was parsed, or null when unknown.</param>
    /// <param name="model">
    ///     The parsed model. It is kept only for <see cref="ConsoleParseOutcomeEnum.Parsed" />.
    /// </param>
    /// <param name="errors">One line per problem. Null is treated as no errors.</param>
    /// <param name="helpText">The text to show the user. Null is treated as empty.</param>
    /// <param name="exception">The unexpected exception behind an invalid result, if any.</param>
    public ConsoleParseResult(
        ConsoleParseOutcomeEnum outcome,
        System.Type inputModelType,
        IConsoleCommandInputModel model = null,
        IEnumerable<string> errors = null,
        string helpText = null,
        System.Exception exception = null
    )
    {
        Outcome = outcome;
        InputModelType = inputModelType;

        // Only a successful parse owns a model, so a failed result can never hand out a half-built one.
        Model = outcome is ConsoleParseOutcomeEnum.Parsed ? model : null;
        Errors = errors is null ? [] : errors.ToList().AsReadOnly();
        HelpText = helpText ?? string.Empty;
        Exception = exception;
    }

    /// <summary>
    ///     This property contains how the parse ended.
    /// </summary>
    public ConsoleParseOutcomeEnum Outcome { get; }

    /// <summary>
    ///     This property contains the input model type that was parsed, or null when the result does
    ///     not belong to one model (for example help requested before any model was chosen).
    /// </summary>
    public System.Type InputModelType { get; }

    /// <summary>
    ///     This property contains the parsed model. It is set only when <see cref="Outcome" /> is
    ///     <see cref="ConsoleParseOutcomeEnum.Parsed" />.
    /// </summary>
    public IConsoleCommandInputModel Model { get; }

    /// <summary>
    ///     This property contains one line per problem, such as
    ///     <c>Missing argument: --name (The name of the thing.)</c>. It is never null and is empty
    ///     unless the outcome is <see cref="ConsoleParseOutcomeEnum.Invalid" /> or
    ///     <see cref="ConsoleParseOutcomeEnum.NotMatched" />.
    /// </summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>
    ///     This property contains the text to show the user. For
    ///     <see cref="ConsoleParseOutcomeEnum.HelpRequested" /> it is the model's help text; for
    ///     <see cref="ConsoleParseOutcomeEnum.Invalid" /> it is the errors followed by the help text;
    ///     otherwise it is empty.
    /// </summary>
    public string HelpText { get; }

    /// <summary>
    ///     This property contains the unexpected exception behind an
    ///     <see cref="ConsoleParseOutcomeEnum.Invalid" /> result (for example a property setter that
    ///     threw), or null.
    /// </summary>
    public System.Exception Exception { get; }

    /// <summary>
    ///     This property is true when <see cref="Outcome" /> is
    ///     <see cref="ConsoleParseOutcomeEnum.Parsed" />.
    /// </summary>
    public bool Succeeded => Outcome is ConsoleParseOutcomeEnum.Parsed;

    /// <summary>
    ///     This property contains the process exit code a host should use for this result: 0 for a
    ///     parsed model or a help request, 1 for anything else. The parser only suggests the code;
    ///     the host owns the exit.
    /// </summary>
    public int ExitCode =>
        Outcome is ConsoleParseOutcomeEnum.Parsed or ConsoleParseOutcomeEnum.HelpRequested ? 0 : 1;
}
