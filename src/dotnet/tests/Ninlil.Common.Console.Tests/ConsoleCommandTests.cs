using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests;

/// <summary>
///     These tests cover <see cref="ConsoleCommand" />: help switches and help text, the service
///     registration helpers, parse-and-dispatch through
///     <see cref="ConsoleCommand.WithParsedAsync{TInputModel}" />, not-parsed handlers, and a run of
///     <see cref="ConsoleCommand.ApplicationAsync{TConsoleApplication}" /> from raw CLI arguments.
///     Every scenario avoids the paths that call <see cref="Environment.Exit" />, which would stop
///     the test host.
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
    ///     Verifies that a <see cref="ConsoleCommand.WithNotParsed{TException}" /> handler for a
    ///     specific exception type (not <see cref="Exception" /> itself) is registered and invoked.
    ///     Storing such a handler with an <c>as</c> cast to
    ///     <c>CommandNotParsedAsyncDelegate&lt;Exception&gt;</c> would give null, so the handler would be
    ///     dropped or throw when invoked. The test also replaces the default <c>Exception</c> handler,
    ///     which calls <see cref="Environment.Exit" />, with a no-op, because every registered handler
    ///     runs on a parse failure, not just the most specific one.
    /// </summary>
    [Fact]
    public async Task WithNotParsed_ForSpecificExceptionType_HandlerIsGenuinelyInvoked()
    {
        bool specificHandlerInvoked = false;

        IConsoleCommand command = ConsoleCommand
            .Create([]) // No arguments for a model with a required one: parsing fails without reaching Environment.Exit.
            .WithNotParsed<Exception>(_ => { })
            .WithNotParsed<InvalidOperationException>(_ => specificHandlerInvoked = true);

        await command.WithParsedAsync<SimpleNamedArgsModel>((_, _, _) => Task.CompletedTask);

        Assert.True(specificHandlerInvoked);
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
