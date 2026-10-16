using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     These tests cover <see cref="ConsoleParseResult" /> and <see cref="ConsoleParseException" />
///     on their own: the exit code per outcome, that only a parsed result keeps its model, that the
///     error list is never null, and how the exception builds its message and lists the results.
///     They construct results directly; the parser's use of them is covered in
///     <see cref="ConsoleCommandArgumentCollectionTests" />.
/// </summary>
public class ConsoleParseResultTests
{
    /// <summary>Verifies the exit code and success flag the result gives for each outcome.</summary>
    [Theory]
    [InlineData(ConsoleParseOutcomeEnum.Parsed, 0, true)]
    [InlineData(ConsoleParseOutcomeEnum.HelpRequested, 0, false)]
    [InlineData(ConsoleParseOutcomeEnum.NotMatched, 1, false)]
    [InlineData(ConsoleParseOutcomeEnum.Invalid, 1, false)]
    public void ExitCode_ForEachOutcome_MatchesTheProcessConvention(
        ConsoleParseOutcomeEnum outcome,
        int exitCode,
        bool succeeded
    )
    {
        ConsoleParseResult result = new(outcome, typeof(AllOptionalModel));

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(succeeded, result.Succeeded);
    }

    /// <summary>Verifies that a result that did not parse never hands out a model.</summary>
    [Fact]
    public void Model_ForAnInvalidResult_IsNull()
    {
        ConsoleParseResult result = new(
            ConsoleParseOutcomeEnum.Invalid,
            typeof(AllOptionalModel),
            new AllOptionalModel()
        );

        Assert.Null(result.Model);
    }

    /// <summary>Verifies that null errors and help text become empty values.</summary>
    [Fact]
    public void Constructor_WithNullErrorsAndHelpText_UsesEmptyValues()
    {
        ConsoleParseResult result = new(ConsoleParseOutcomeEnum.Parsed, null);

        Assert.Empty(result.Errors);
        Assert.Equal(string.Empty, result.HelpText);
    }

    /// <summary>Verifies that the exception's message is the help text, its inner exception is the result's, and it lists the result.</summary>
    [Fact]
    public void ConsoleParseException_ForAnInvalidResult_UsesHelpTextAndInnerException()
    {
        ArgumentException cause = new("bad");
        ConsoleParseResult result = new(
            ConsoleParseOutcomeEnum.Invalid,
            typeof(AllOptionalModel),
            errors: ["bad"],
            helpText: "bad" + Environment.NewLine + "usage",
            exception: cause
        );

        ConsoleParseException exception = new(result);

        Assert.Equal(result.HelpText, exception.Message);
        Assert.Same(cause, exception.InnerException);
        Assert.Same(result, Assert.Single(exception.Results));
        Assert.IsAssignableFrom<InvalidOperationException>(exception);
    }

    /// <summary>Verifies that the exception says no model matched when every result is not matched.</summary>
    [Fact]
    public void ConsoleParseException_WhenEveryResultIsNotMatched_SaysNoInputModelMatched()
    {
        ConsoleParseResult first = new(ConsoleParseOutcomeEnum.NotMatched, typeof(VerbModel));
        ConsoleParseResult second = new(
            ConsoleParseOutcomeEnum.NotMatched,
            typeof(AllOptionalModel)
        );

        ConsoleParseException exception = new(first, [first, second]);

        Assert.StartsWith("No input model matched", exception.Message);
        Assert.Equal(2, exception.Results.Count);
    }

    /// <summary>Verifies that the exception refuses a null result.</summary>
    [Fact]
    public void ConsoleParseException_WithNullResult_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ConsoleParseException(null));
    }
}
