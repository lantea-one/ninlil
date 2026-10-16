using Microsoft.Extensions.DependencyInjection;
using Ninlil.Common.Console;
using Ninlil.Common.Console.Model;

// Define our namespace.
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
///     This test double records the input it ran with in a static property, so a test can drive
///     <see cref="IConsoleCommand.ApplicationAsync{TConsoleApplication}" /> from raw CLI arguments
///     (which creates the instance itself) and still inspect the result.
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

/// <summary>
///     This test double resolves <see cref="IConsoleLogger" /> from the service provider inside
///     <c>RunAsync</c>, so a test can check that the command gives a console application its
///     provider. It registers a logger in <see cref="ConfigureServices" />, which only
///     <see cref="ConsoleApplication.ExecuteAsync(IEnumerable{string}, CancellationToken)" /> calls;
///     a run through <see cref="IConsoleCommand.ApplicationAsync{TConsoleApplication}" /> needs the
///     test to register the logger on the command. The result lands in a static property because
///     the command creates the instance itself.
/// </summary>
public class ServiceResolvingTestConsoleApplication : ConsoleApplication<SimpleNamedArgsModel>
{
    /// <summary>The logger the most recent run resolved from the service provider.</summary>
    public static IConsoleLogger LastResolvedLogger { get; set; }

    /// <summary>Initializes a new instance backed by a real <see cref="ConsoleLogger" />.</summary>
    public ServiceResolvingTestConsoleApplication()
        : base(ConsoleLogger.Create()) { }

    /// <inheritdoc />
    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IConsoleLogger>(_ => ConsoleLogger.Create());

    /// <inheritdoc />
    public override Task RunAsync(
        SimpleNamedArgsModel input,
        CancellationToken stoppingToken = default
    )
    {
        LastResolvedLogger = GetService<IConsoleLogger>();
        return Task.CompletedTask;
    }
}
