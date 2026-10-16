using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests;

/// <summary>
///     These tests cover <see cref="ConsoleCommand" />: help switches and help text, the service
///     registration helpers, parse-and-dispatch through
///     <see cref="ConsoleCommand.WithParsedAsync{TInputModel}" />, not-parsed handlers, and a run of
///     <see cref="ConsoleCommand.ApplicationAsync{TConsoleApplication}" /> from raw CLI arguments.
///     The runner never ends the process, so the help, failure and default-handler paths run in
///     process and the tests assert on <see cref="ConsoleCommand.ExitCode" />,
///     <see cref="ConsoleCommand.LastParseResult" /> and handler arguments. The help tests write to
///     standard output without capturing it; the default-handler test captures standard error.
/// </summary>
[Collection(ConsoleCommandTests.CollectionName)]
public class ConsoleCommandTests
{
    /// <summary>
    ///     This constant names the xUnit collection of this class. The collection runs its classes
    ///     one at a time because one test swaps <see cref="System.Console.Error" />, which is process-wide
    ///     state.
    /// </summary>
    public const string CollectionName = "Console command process state";

    /// <summary>Verifies that <see cref="ConsoleCommand.HelpRequestSwitches" /> contains the documented switches.</summary>
    [Fact]
    public void HelpRequestSwitches_ContainsExpectedSwitches()
    {
        Assert.Contains("?", ConsoleCommand.HelpRequestSwitches);
        Assert.Contains("help", ConsoleCommand.HelpRequestSwitches);
        Assert.Contains("version", ConsoleCommand.HelpRequestSwitches);
    }

    /// <summary>Verifies that <see cref="ConsoleCommand.Create" /> returns a usable instance.</summary>
    [Fact]
    public void Create_ReturnsUsableInstance()
    {
        IConsoleCommand command = ConsoleCommand.Create(["--name", "John"]);

        Assert.NotNull(command);
    }

    /// <summary>Verifies that <see cref="ConsoleCommand.GetHelpText(Type[])" /> produces non-empty output for a given type.</summary>
    [Fact]
    public void GetHelpTextStatic_ProducesNonEmptyOutput()
    {
        string result = ConsoleCommand.GetHelpText(typeof(SimpleNamedArgsModel));

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("name", result, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Verifies the generic single-type-parameter <see cref="ConsoleCommand.GetHelpText{TInputModel1}" /> overload.</summary>
    [Fact]
    public void GetHelpTextStatic_Generic_ProducesNonEmptyOutput()
    {
        string result = ConsoleCommand.GetHelpText<SimpleNamedArgsModel>();

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    /// <summary>Verifies that <see cref="ConsoleCommand.AddSingletonService{TImplementation}" /> registers a resolvable service.</summary>
    [Fact]
    public void AddSingletonService_RegistersResolvableService()
    {
        IConsoleCommand command = ConsoleCommand.Create([]).AddSingletonService<List<string>>();

        List<string> first = command.GetService<List<string>>();
        List<string> second = command.GetService<List<string>>();

        Assert.NotNull(first);
        Assert.NotSame(first, second); // GetService builds a fresh provider each call, so singleton scoping isn't observable across calls.
    }

    /// <summary>Verifies that <see cref="ConsoleCommand.AddSingletonService{TInterface, TImplementation}" /> registers under the interface type.</summary>
    [Fact]
    public void AddSingletonServiceWithInterface_RegistersUnderInterfaceType()
    {
        IConsoleCommand command = ConsoleCommand
            .Create([])
            .AddSingletonService<IList<string>, List<string>>();

        IList<string> result = command.GetService<IList<string>>();

        Assert.IsType<List<string>>(result);
    }

    /// <summary>Verifies that <see cref="ConsoleCommand.AddScopedService{TImplementation}" /> registers a resolvable service.</summary>
    [Fact]
    public void AddScopedService_RegistersResolvableService()
    {
        IConsoleCommand command = ConsoleCommand.Create([]).AddScopedService<List<string>>();

        Assert.NotNull(command.GetService<List<string>>());
    }

    /// <summary>Verifies that <see cref="ConsoleCommand.AddTransientService{TImplementation}" /> registers a resolvable service.</summary>
    [Fact]
    public void AddTransientService_RegistersResolvableService()
    {
        IConsoleCommand command = ConsoleCommand.Create([]).AddTransientService<List<string>>();

        Assert.NotNull(command.GetService<List<string>>());
    }

    /// <summary>Verifies that a service registered via a factory delegate is resolvable.</summary>
    [Fact]
    public void AddSingletonService_WithFactory_UsesFactory()
    {
        IConsoleCommand command = ConsoleCommand
            .Create([])
            .AddSingletonService<string>(_ => "factory-produced");

        Assert.Equal("factory-produced", command.GetService<string>());
    }

    /// <summary>Verifies that <see cref="ConsoleCommand.ConfigureServices" /> invokes the provided handler with the underlying service collection.</summary>
    [Fact]
    public void ConfigureServices_InvokesHandlerWithServiceCollection()
    {
        bool invoked = false;

        ConsoleCommand.Create([]).ConfigureServices(_ => invoked = true);

        Assert.True(invoked);
    }

    /// <summary>Verifies that a successfully parsed model reaches the <see cref="IConsoleCommand.WithParsedAsync{TInputModel}" /> delegate.</summary>
    [Fact]
    public async Task WithParsedAsync_WithValidArguments_InvokesParsedDelegate()
    {
        SimpleNamedArgsModel receivedInput = null;

        await ConsoleCommand
            .Create(["--name", "John"])
            .WithParsedAsync<SimpleNamedArgsModel>(
                (input, _, _) =>
                {
                    receivedInput = input;
                    return Task.CompletedTask;
                }
            );

        Assert.NotNull(receivedInput);
        Assert.Equal("John", receivedInput.Name);
    }

    /// <summary>
    ///     Verifies that a <see cref="ConsoleCommand.WithNotParsed{TException}" /> handler for a
    ///     specific exception type (not <see cref="Exception" /> itself) is registered and invoked.
    ///     Storing such a handler with an <c>as</c> cast to
    ///     <c>CommandNotParsedAsyncDelegate&lt;Exception&gt;</c> would give null, so the handler would be
    ///     dropped or throw when invoked. The test also replaces the default <c>Exception</c> handler
    ///     with a no-op to keep the console quiet, because every registered handler runs on a parse
    ///     failure, not just the most specific one.
    /// </summary>
    [Fact]
    public async Task WithNotParsed_ForSpecificExceptionType_HandlerIsGenuinelyInvoked()
    {
        bool specificHandlerInvoked = false;

        IConsoleCommand command = ConsoleCommand
            .Create([]) // No arguments for a model with a required one: parsing fails.
            .WithNotParsed<Exception>(_ => { })
            .WithNotParsed<InvalidOperationException>(_ => specificHandlerInvoked = true);

        await command.WithParsedAsync<SimpleNamedArgsModel>((_, _, _) => Task.CompletedTask);

        Assert.True(specificHandlerInvoked);
    }

    /// <summary>
    ///     Verifies that the second model is tried when the first lacks a required argument, so the
    ///     first model's failure does not end the run.
    /// </summary>
    [Fact]
    public async Task WithParsedAsync_TwoModels_TriesTheSecondWhenTheFirstIsInvalid()
    {
        IConsoleCommandInputModel received = null;
        ConsoleCommand command = ConsoleCommand.Create(["--label", "x"]);

        await command.WithParsedAsync<SimpleNamedArgsModel, AllOptionalModel>(
            (input, _, _) =>
            {
                received = input;
                return Task.CompletedTask;
            }
        );

        AllOptionalModel model = Assert.IsType<AllOptionalModel>(received);
        Assert.Equal("x", model.Label);
        Assert.Equal(0, command.ExitCode);
        Assert.Equal(ConsoleParseOutcomeEnum.Parsed, command.LastParseResult.Outcome);
    }

    /// <summary>
    ///     Verifies that a help switch calls neither the delegate nor a not-parsed handler and
    ///     reports exit code 0.
    /// </summary>
    [Fact]
    public async Task WithParsedAsync_WithHelpSwitch_PrintsHelpAndReportsExitCodeZero()
    {
        bool delegateCalled = false;
        bool handlerCalled = false;
        ConsoleCommand command = ConsoleCommand.Create(["--help"]);
        command.WithNotParsed<Exception>(_ => handlerCalled = true);

        await command.WithParsedAsync<SimpleNamedArgsModel>(
            (_, _, _) =>
            {
                delegateCalled = true;
                return Task.CompletedTask;
            }
        );

        Assert.False(delegateCalled);
        Assert.False(handlerCalled);
        Assert.Equal(0, command.ExitCode);
        Assert.Equal(ConsoleParseOutcomeEnum.HelpRequested, command.LastParseResult.Outcome);
        Assert.Contains("--name", command.LastParseResult.HelpText);
    }

    /// <summary>
    ///     Verifies that a help switch after a verb reports a help request, not a failure.
    /// </summary>
    [Fact]
    public async Task WithParsedAsync_WithHelpSwitchAfterVerb_ReportsHelpWithoutHandlers()
    {
        bool handlerCalled = false;
        ConsoleCommand command = ConsoleCommand.Create(["add", "--help"]);
        command.WithNotParsed<Exception>(_ => handlerCalled = true);

        await command.WithParsedAsync<VerbModel>((_, _, _) => Task.CompletedTask);

        Assert.False(handlerCalled);
        Assert.Equal(0, command.ExitCode);
        Assert.Equal(ConsoleParseOutcomeEnum.HelpRequested, command.LastParseResult.Outcome);
    }

    /// <summary>
    ///     Verifies that a failed parse gives the handlers one <see cref="ConsoleParseException" />
    ///     that names the missing argument, and sets exit code 1.
    /// </summary>
    [Fact]
    public async Task WithParsedAsync_WithMissingArgument_CallsHandlersWithParseExceptionAndExitCodeOne()
    {
        ConsoleParseException received = null;
        ConsoleCommand command = ConsoleCommand.Create(["--age", "3"]);
        command.WithNotParsed<Exception>(_ => { });
        command.WithNotParsed<InvalidOperationException>(e =>
            received = e as ConsoleParseException
        );

        await command.WithParsedAsync<SimpleNamedArgsModel>((_, _, _) => Task.CompletedTask);

        Assert.NotNull(received);
        Assert.Equal(ConsoleParseOutcomeEnum.Invalid, received.Result.Outcome);
        Assert.Contains(received.Result.Errors, e => e.Contains("--name"));
        Assert.Same(received.Result, command.LastParseResult);
        Assert.Equal(1, command.ExitCode);
    }

    /// <summary>
    ///     Verifies that the handlers see every result tried and that the first invalid result is
    ///     the one reported.
    /// </summary>
    [Fact]
    public async Task WithParsedAsync_TwoFailingModels_ReportsTheFirstInvalidResultAndAllResults()
    {
        ConsoleParseException received = null;
        ConsoleCommand command = ConsoleCommand.Create(["--age", "3"]);
        command.WithNotParsed<Exception>(e => received = e as ConsoleParseException);

        await command.WithParsedAsync<SimpleNamedArgsModel, PositionalArgsModel>(
            (_, _, _) => Task.CompletedTask
        );

        Assert.NotNull(received);
        Assert.Equal(2, received.Results.Count);
        Assert.Equal(typeof(SimpleNamedArgsModel), received.Result.InputModelType);
        Assert.Equal(1, command.ExitCode);
    }

    /// <summary>
    ///     Verifies that the default not-parsed handler writes to standard error and returns
    ///     instead of ending the process. The error stream is redirected to keep test output clean
    ///     and restored in <c>finally</c>; no other test class touches it concurrently because the
    ///     collection is serial.
    /// </summary>
    [Fact]
    public async Task WithParsedAsync_WithNoHandlerRegistered_DefaultHandlerWritesToErrorAndReturns()
    {
        TextWriter original = System.Console.Error;
        StringWriter captured = new();
        ConsoleCommand command = ConsoleCommand.Create([]);

        try
        {
            System.Console.SetError(captured);
            await command.WithParsedAsync<SimpleNamedArgsModel>((_, _, _) => Task.CompletedTask);
        }
        finally
        {
            System.Console.SetError(original);
        }

        Assert.Equal(1, command.ExitCode);
        Assert.Contains("--name", captured.ToString());
    }

    /// <summary>
    ///     Verifies that empty and blank first arguments no longer throw (they are positional values)
    ///     and that a model with only optional arguments runs.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WithParsedAsync_WithBlankFirstArgument_RunsTheDelegate(string argument)
    {
        bool delegateCalled = false;
        ConsoleCommand command = ConsoleCommand.Create([argument]);

        await command.WithParsedAsync<AllOptionalModel>(
            (_, _, _) =>
            {
                delegateCalled = true;
                return Task.CompletedTask;
            }
        );

        Assert.True(delegateCalled);
        Assert.Equal(0, command.ExitCode);
    }

    /// <summary>
    ///     Verifies that the first argument selects the model with that verb, so the verb model wins
    ///     over a model that would otherwise be tried first.
    /// </summary>
    [Fact]
    public async Task WithParsedAsync_WithVerbArgument_NarrowsToTheVerbModel()
    {
        IConsoleCommandInputModel received = null;

        await ConsoleCommand
            .Create(["add", "--name", "w"])
            .WithParsedAsync<SimpleNamedArgsModel, VerbModel>(
                (input, _, _) =>
                {
                    received = input;
                    return Task.CompletedTask;
                }
            );

        Assert.IsType<VerbModel>(received);
    }

    /// <summary>
    ///     Verifies that the exit code is 0 and the last result is null before any run.
    /// </summary>
    [Fact]
    public void ExitCode_BeforeAnyRun_IsZeroWithNoResult()
    {
        ConsoleCommand command = ConsoleCommand.Create([]);

        Assert.Equal(0, command.ExitCode);
        Assert.Null(command.LastParseResult);
    }

    /// <summary>
    ///     Verifies an end-to-end run: raw CLI arguments are parsed and dispatched through
    ///     <see cref="ConsoleCommand.ApplicationAsync{TConsoleApplication}" /> to
    ///     <see cref="EndToEndTestConsoleApplication" />'s <c>RunAsync</c>.
    /// </summary>
    [Fact]
    public async Task ApplicationAsync_Generic_RunsRealConsoleApplicationEndToEnd()
    {
        await ConsoleCommand
            .Create(["--name", "Alice"])
            .ApplicationAsync<EndToEndTestConsoleApplication>();

        Assert.NotNull(EndToEndTestConsoleApplication.LastReceivedInput);
        Assert.Equal("Alice", EndToEndTestConsoleApplication.LastReceivedInput.Name);
    }

    /// <summary>Verifies that <see cref="ConsoleCommand.ApplicationAsync(Type, CancellationToken)" /> throws for a type that isn't an <see cref="IConsoleApplication" />.</summary>
    [Fact]
    public async Task ApplicationAsync_WithInvalidType_Throws()
    {
        IConsoleCommand command = ConsoleCommand.Create([]);

        await Assert.ThrowsAsync<ArgumentException>(() => command.ApplicationAsync(typeof(string)));
    }
}
