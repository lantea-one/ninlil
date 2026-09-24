using Ninlil.Common.Console;
using Ninlil.Common.Console.Model;

// Define our namespace for the shared test console-application doubles.
namespace Ninlil.Common.Console.Tests.TestSupport;

/// <summary>
///     This test double records the input model it was run with, so a test can assert that
///     <see cref="ConsoleApplication.ExecuteAsync(IConsoleCommandInputModel, CancellationToken)" />'s
///     reflection-based dispatch found and invoked the correct <see cref="RunAsync" /> overload.
/// </summary>
public class TestConsoleApplication : ConsoleApplication<SimpleNamedArgsModel>
{
    /// <summary>The input model instance most recently passed to <see cref="RunAsync" />.</summary>
    public SimpleNamedArgsModel ReceivedInput { get; private set; }

    /// <summary>The number of times <see cref="RunAsync" /> has been invoked.</summary>
    public int RunCount { get; private set; }

    /// <summary>Initializes a new instance backed by a real <see cref="ConsoleLogger" />.</summary>
    public TestConsoleApplication()
        : base(ConsoleLogger.Create()) { }

    /// <summary>Exposes the protected <see cref="ConsoleApplication.GetService{TService}()" /> for direct testing.</summary>
    /// <typeparam name="TService">The type of the service to retrieve.</typeparam>
    /// <returns>The resolved service instance.</returns>
    public TService ResolveService<TService>()
        where TService : class => GetService<TService>();

    /// <inheritdoc />
    public override Task RunAsync(
        SimpleNamedArgsModel input,
        CancellationToken stoppingToken = default
    )
    {
        ReceivedInput = input;
        ++RunCount;
        return Task.CompletedTask;
    }
}

/// <summary>
///     This test double records whether it ran, and with what input, exercising a full real
///     end-to-end <see cref="IConsoleCommand.ApplicationAsync{TConsoleApplication}" /> dispatch
///     starting from raw CLI arguments.
/// </summary>
public class EndToEndTestConsoleApplication : ConsoleApplication<SimpleNamedArgsModel>
{
    /// <summary>The input model instance the application was run with.</summary>
    public static SimpleNamedArgsModel LastReceivedInput { get; private set; }

    /// <summary>Initializes a new instance backed by a real <see cref="ConsoleLogger" />.</summary>
    public EndToEndTestConsoleApplication()
        : base(ConsoleLogger.Create()) { }

    /// <inheritdoc />
    public override Task RunAsync(
        SimpleNamedArgsModel input,
        CancellationToken stoppingToken = default
    )
    {
        LastReceivedInput = input;
        return Task.CompletedTask;
    }
}
