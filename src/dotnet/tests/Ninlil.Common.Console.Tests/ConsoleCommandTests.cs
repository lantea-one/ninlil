using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace for the console-command tests.
namespace Ninlil.Common.Console.Tests;

/// <summary>
///     This test class exercises <see cref="ConsoleCommand" /> - static application metadata,
///     the DI-registration helpers, <see cref="ConsoleCommand.WithParsedAsync{TInputModel}" />'s
///     parse-and-dispatch orchestration, <see cref="ConsoleCommand.WithNotParsed{TException}" /> (a
///     real bug fixed in this project), and a full end-to-end
///     <see cref="ConsoleCommand.ApplicationAsync{TConsoleApplication}" /> run starting from raw CLI
///     arguments. As elsewhere, every scenario avoids ever triggering the real process-exit paths.
/// </summary>
public class ConsoleCommandTests
{
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
    ///     Verifies that <see cref="ConsoleCommand.WithNotParsed{TException}" /> for a specific exception
    ///     type (not exactly <see cref="Exception" />) is genuinely registered and invoked. This is a
    ///     real bug found and fixed in this project: the original implementation tried to store the
    ///     handler via a direct <c>as</c> cast to <c>CommandNotParsedAsyncDelegate&lt;Exception&gt;</c>,
    ///     an unsound delegate-variance conversion that silently evaluates to null for any exception
    ///     type more specific than exactly <c>Exception</c> - which either dropped the handler
    ///     entirely, or threw a <see cref="NullReferenceException" /> the moment the dispatch loop tried
    ///     to invoke the null delegate it had stored. The scenario also explicitly overwrites the
    ///     constructor's own default <c>Exception</c>-keyed handler (which calls
    ///     <see cref="Environment.Exit" /> via <c>ConsoleService.WriteLine(exception, true)</c>) with a
    ///     harmless no-op, since every registered handler is invoked on a parse failure, not just the
    ///     most specific match.
    /// </summary>
    [Fact]
    public async Task WithNotParsed_ForSpecificExceptionType_HandlerIsGenuinelyInvoked()
    {
        bool specificHandlerInvoked = false;

        IConsoleCommand command = ConsoleCommand
            .Create([]) // zero arguments against a model with a required argument - triggers a real parse failure without ever calling Environment.Exit.
            .WithNotParsed<Exception>(_ => { })
            .WithNotParsed<InvalidOperationException>(_ => specificHandlerInvoked = true);

        await command.WithParsedAsync<SimpleNamedArgsModel>((_, _, _) => Task.CompletedTask);

        Assert.True(specificHandlerInvoked);
    }

    /// <summary>
    ///     Verifies a full, real end-to-end run: raw CLI arguments are parsed and dispatched through
    ///     <see cref="ConsoleCommand.ApplicationAsync{TConsoleApplication}" /> to a real
    ///     <see cref="EndToEndTestConsoleApplication" />'s <c>RunAsync</c> implementation.
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
