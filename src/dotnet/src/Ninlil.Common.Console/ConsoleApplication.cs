using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console;

/// <summary>
///     This abstract class is the base for console applications built on this library. It holds
///     the logger and the service provider, exposes service lookup to subclasses, and turns a
///     parsed input model into a call to the matching <c>RunAsync</c> overload by reflection. The
///     generic subclasses declare which input models (verbs) the application accepts; each adds
///     one abstract <c>RunAsync</c> overload for the application to implement. A run from a
///     <see cref="ConsoleCommand" /> (<see cref="ConsoleCommand.ApplicationAsync(Type, CancellationToken)" />
///     or <see cref="ExecuteAsync(IEnumerable{string}, CancellationToken)" />) hands the application
///     the service provider scope of that run through <see cref="WithServiceProvider" />, so
///     <c>GetService</c> works inside <c>RunAsync</c>; the scope ends when the run ends.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
public abstract class ConsoleApplication(IConsoleLogger logger) : IConsoleApplication
{
    /// <summary>
    ///     This field contains the service provider for the current run, set through
    ///     <see cref="WithServiceProvider" />.
    /// </summary>
    private IServiceProvider _serviceProvider;

    /// <summary>
    ///     This field contains the logger for the console application.
    /// </summary>
    protected IConsoleLogger Logger = logger;

    /// <summary>
    ///     This method lets a subclass register its services. It runs before the command line is
    ///     parsed, when <see cref="ExecuteAsync(IEnumerable{string}, CancellationToken)" /> builds
    ///     the <see cref="IConsoleCommand" />.
    /// </summary>
    /// <param name="services">The dependency injection container.</param>
    protected virtual void ConfigureServices(IServiceCollection services) { }

    /// <summary>
    ///     This method resolves a required service from the service provider.
    /// </summary>
    /// <typeparam name="TService">The type of the service to retrieve.</typeparam>
    /// <returns>The service from the service provider.</returns>
    protected TService GetService<TService>()
        where TService : class => _serviceProvider.GetRequiredService<TService>();

    /// <summary>
    ///     This method resolves a required keyed service from the service provider.
    /// </summary>
    /// <param name="key">The key to identify the service.</param>
    /// <typeparam name="TService">The type of the service to retrieve.</typeparam>
    /// <returns>The <paramref name="key" /> service from the service provider.</returns>
    protected TService GetService<TService>(object key)
        where TService : class => _serviceProvider.GetRequiredKeyedService<TService>(key);

    /// <summary>
    ///     This method runs the application with an already parsed <paramref name="input" />. It finds
    ///     the public <c>RunAsync</c> overload whose first parameter is the runtime type of
    ///     <paramref name="input" /> and invokes it.
    /// </summary>
    /// <param name="input">The parsed input model from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task ExecuteAsync(
        IConsoleCommandInputModel input,
        CancellationToken stoppingToken = default
    )
    {
        // Find the RunAsync overload declared for this input model type.
        MethodInfo method = GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name.Equals("RunAsync", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(m => m.GetParameters().First().ParameterType == input.GetType());

        // If we don't have a method, throw an exception.
        if (method is null)
            throw new NotSupportedException(
                $"The console application does not support this operation [RunAsync({input.GetType().Name}, CancellationToken)]."
            );

        // Execute the method with the typed input model and the cancellation token.
        return (Task)method.Invoke(this, [input, stoppingToken]);
    }

    /// <summary>
    ///     This method parses the raw command line <paramref name="arguments" /> against the input
    ///     models named by this class's generic arguments, gives the application the service provider
    ///     scope of the run and then calls
    ///     <see cref="ExecuteAsync(IConsoleCommandInputModel, CancellationToken)" />. It builds a
    ///     <see cref="ConsoleCommand" /> for the run, applies <see cref="ConfigureServices" /> and
    ///     disposes the command (and its singletons) when the run ends. A command line that no input
    ///     model accepts is reported through the command's default not-parsed handler.
    /// </summary>
    /// <param name="arguments">The command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public async Task ExecuteAsync(
        IEnumerable<string> arguments,
        CancellationToken stoppingToken = default
    )
    {
        // The command owns the root service provider, so it is disposed when the run ends.
        await using ConsoleCommand command = ConsoleCommand.Create(arguments);
        command.ConfigureServices(ConfigureServices);

        // Run through the command's own parse-and-dispatch body with this class's input model types.
        await command.RunParsedAsync(
            ConsoleCommand.GetConsoleApplicationInputModelTypes(GetType()),
            (input, serviceProvider, token) =>
            {
                // Store the scope provider built for this run, so GetService works in RunAsync.
                WithServiceProvider(serviceProvider);
                return ExecuteAsync(input, token);
            },
            stoppingToken
        );
    }

    /// <summary>
    ///     This method sets the application name shown in the help text. It writes the static
    ///     <see cref="ConsoleCommand.Application" />, so it applies process-wide.
    /// </summary>
    /// <param name="applicationName">The name of the application.</param>
    public void WithApplication(string applicationName) =>
        ConsoleCommand.Application = applicationName?.Trim();

    /// <summary>
    ///     This method sets the author shown in the help text (process-wide, through
    ///     <see cref="ConsoleCommand.Author" />).
    /// </summary>
    /// <param name="author">The author information for the application.</param>
    public void WithAuthor(string author) => ConsoleCommand.Author = author?.Trim();

    /// <summary>
    ///     This method sets the company shown in the help text (process-wide, through
    ///     <see cref="ConsoleCommand.Company" />).
    /// </summary>
    /// <param name="company">The company information for the application.</param>
    public void WithCompany(string company) => ConsoleCommand.Company = company?.Trim();

    /// <summary>
    ///     This method sets the description shown in the help text (process-wide, through
    ///     <see cref="ConsoleCommand.Description" />).
    /// </summary>
    /// <param name="description">The description information for the application.</param>
    public void WithDescription(string description) =>
        ConsoleCommand.Description = description?.Trim();

    /// <summary>
    ///     This method sets the service provider the application resolves its services from.
    /// </summary>
    /// <param name="serviceProvider">The service provider built for this run.</param>
    public void WithServiceProvider(IServiceProvider serviceProvider) =>
        _serviceProvider = serviceProvider;

    /// <summary>
    ///     This method sets the version shown in the help text (process-wide, through
    ///     <see cref="ConsoleCommand.Version" />).
    /// </summary>
    /// <param name="version">The version information for the application.</param>
    public void WithVersion(string version) => ConsoleCommand.Version = version?.Trim();
}

/// <summary>
///     This class is a console application that accepts one input model. The variants with more
///     type parameters each add one more input model and one more abstract <c>RunAsync</c>
///     overload.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel">The type of the input model.</typeparam>
public abstract class ConsoleApplication<TInputModel>(IConsoleLogger logger)
    : ConsoleApplication(logger),
        IConsoleApplication<TInputModel>
    where TInputModel : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with two input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
public abstract class ConsoleApplication<TInputModel1, TInputModel2>(IConsoleLogger logger)
    : ConsoleApplication<TInputModel1>(logger),
        IConsoleApplication<TInputModel1, TInputModel2>
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel2" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel2" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel2 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with three input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
public abstract class ConsoleApplication<TInputModel1, TInputModel2, TInputModel3>(
    IConsoleLogger logger
)
    : ConsoleApplication<TInputModel1, TInputModel2>(logger),
        IConsoleApplication<TInputModel1, TInputModel2, TInputModel3>
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel3" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel3" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel3 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with four input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
public abstract class ConsoleApplication<TInputModel1, TInputModel2, TInputModel3, TInputModel4>(
    IConsoleLogger logger
)
    : ConsoleApplication<TInputModel1, TInputModel2, TInputModel3>(logger),
        IConsoleApplication<TInputModel1, TInputModel2, TInputModel3, TInputModel4>
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel4" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel4" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel4 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with five input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5
>(IConsoleLogger logger)
    : ConsoleApplication<TInputModel1, TInputModel2, TInputModel3, TInputModel4>(logger),
        IConsoleApplication<TInputModel1, TInputModel2, TInputModel3, TInputModel4, TInputModel5>
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel5" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel5" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel5 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with six input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6
>(IConsoleLogger logger)
    : ConsoleApplication<TInputModel1, TInputModel2, TInputModel3, TInputModel4, TInputModel5>(
        logger
    ),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel6" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel6" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel6 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with seven input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel7" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel7" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel7 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with eight input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel8" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel8" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel8 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with nine input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel9" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel9" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel9 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with ten input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel10" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel10" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel10 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with eleven input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel11" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel11" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel11 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with twelve input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel12" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel12" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel12 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with thirteen input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel13" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel13" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel13 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with fourteen input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel14" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel14" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel14 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with fifteen input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel15" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel15" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel15 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with sixteen input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel16" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel16" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel16 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with seventeen input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
/// <typeparam name="TInputModel17">The type of the seventeenth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16,
    TInputModel17
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15,
        TInputModel16
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16,
            TInputModel17
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
    where TInputModel17 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel17" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel17" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel17 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with eighteen input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
/// <typeparam name="TInputModel17">The type of the seventeenth input model.</typeparam>
/// <typeparam name="TInputModel18">The type of the eighteenth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16,
    TInputModel17,
    TInputModel18
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15,
        TInputModel16,
        TInputModel17
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16,
            TInputModel17,
            TInputModel18
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
    where TInputModel17 : class, IConsoleCommandInputModel
    where TInputModel18 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel18" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel18" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel18 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with nineteen input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
/// <typeparam name="TInputModel17">The type of the seventeenth input model.</typeparam>
/// <typeparam name="TInputModel18">The type of the eighteenth input model.</typeparam>
/// <typeparam name="TInputModel19">The type of the nineteenth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16,
    TInputModel17,
    TInputModel18,
    TInputModel19
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15,
        TInputModel16,
        TInputModel17,
        TInputModel18
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16,
            TInputModel17,
            TInputModel18,
            TInputModel19
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
    where TInputModel17 : class, IConsoleCommandInputModel
    where TInputModel18 : class, IConsoleCommandInputModel
    where TInputModel19 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel19" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel19" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel19 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with twenty input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
/// <typeparam name="TInputModel17">The type of the seventeenth input model.</typeparam>
/// <typeparam name="TInputModel18">The type of the eighteenth input model.</typeparam>
/// <typeparam name="TInputModel19">The type of the nineteenth input model.</typeparam>
/// <typeparam name="TInputModel20">The type of the twentieth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16,
    TInputModel17,
    TInputModel18,
    TInputModel19,
    TInputModel20
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15,
        TInputModel16,
        TInputModel17,
        TInputModel18,
        TInputModel19
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16,
            TInputModel17,
            TInputModel18,
            TInputModel19,
            TInputModel20
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
    where TInputModel17 : class, IConsoleCommandInputModel
    where TInputModel18 : class, IConsoleCommandInputModel
    where TInputModel19 : class, IConsoleCommandInputModel
    where TInputModel20 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel20" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel20" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel20 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with twenty-one input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
/// <typeparam name="TInputModel17">The type of the seventeenth input model.</typeparam>
/// <typeparam name="TInputModel18">The type of the eighteenth input model.</typeparam>
/// <typeparam name="TInputModel19">The type of the nineteenth input model.</typeparam>
/// <typeparam name="TInputModel20">The type of the twentieth input model.</typeparam>
/// <typeparam name="TInputModel21">The type of the twenty-first input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16,
    TInputModel17,
    TInputModel18,
    TInputModel19,
    TInputModel20,
    TInputModel21
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15,
        TInputModel16,
        TInputModel17,
        TInputModel18,
        TInputModel19,
        TInputModel20
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16,
            TInputModel17,
            TInputModel18,
            TInputModel19,
            TInputModel20,
            TInputModel21
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
    where TInputModel17 : class, IConsoleCommandInputModel
    where TInputModel18 : class, IConsoleCommandInputModel
    where TInputModel19 : class, IConsoleCommandInputModel
    where TInputModel20 : class, IConsoleCommandInputModel
    where TInputModel21 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel21" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel21" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel21 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with twenty-two input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
/// <typeparam name="TInputModel17">The type of the seventeenth input model.</typeparam>
/// <typeparam name="TInputModel18">The type of the eighteenth input model.</typeparam>
/// <typeparam name="TInputModel19">The type of the nineteenth input model.</typeparam>
/// <typeparam name="TInputModel20">The type of the twentieth input model.</typeparam>
/// <typeparam name="TInputModel21">The type of the twenty-first input model.</typeparam>
/// <typeparam name="TInputModel22">The type of the twenty-second input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16,
    TInputModel17,
    TInputModel18,
    TInputModel19,
    TInputModel20,
    TInputModel21,
    TInputModel22
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15,
        TInputModel16,
        TInputModel17,
        TInputModel18,
        TInputModel19,
        TInputModel20,
        TInputModel21
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16,
            TInputModel17,
            TInputModel18,
            TInputModel19,
            TInputModel20,
            TInputModel21,
            TInputModel22
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
    where TInputModel17 : class, IConsoleCommandInputModel
    where TInputModel18 : class, IConsoleCommandInputModel
    where TInputModel19 : class, IConsoleCommandInputModel
    where TInputModel20 : class, IConsoleCommandInputModel
    where TInputModel21 : class, IConsoleCommandInputModel
    where TInputModel22 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel22" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel22" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel22 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with twenty-three input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
/// <typeparam name="TInputModel17">The type of the seventeenth input model.</typeparam>
/// <typeparam name="TInputModel18">The type of the eighteenth input model.</typeparam>
/// <typeparam name="TInputModel19">The type of the nineteenth input model.</typeparam>
/// <typeparam name="TInputModel20">The type of the twentieth input model.</typeparam>
/// <typeparam name="TInputModel21">The type of the twenty-first input model.</typeparam>
/// <typeparam name="TInputModel22">The type of the twenty-second input model.</typeparam>
/// <typeparam name="TInputModel23">The type of the twenty-third input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16,
    TInputModel17,
    TInputModel18,
    TInputModel19,
    TInputModel20,
    TInputModel21,
    TInputModel22,
    TInputModel23
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15,
        TInputModel16,
        TInputModel17,
        TInputModel18,
        TInputModel19,
        TInputModel20,
        TInputModel21,
        TInputModel22
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16,
            TInputModel17,
            TInputModel18,
            TInputModel19,
            TInputModel20,
            TInputModel21,
            TInputModel22,
            TInputModel23
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
    where TInputModel17 : class, IConsoleCommandInputModel
    where TInputModel18 : class, IConsoleCommandInputModel
    where TInputModel19 : class, IConsoleCommandInputModel
    where TInputModel20 : class, IConsoleCommandInputModel
    where TInputModel21 : class, IConsoleCommandInputModel
    where TInputModel22 : class, IConsoleCommandInputModel
    where TInputModel23 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel23" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel23" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel23 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This class maintains the structure of a console application with twenty-four input models.
/// </summary>
/// <param name="logger">The logger for the console application.</param>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
/// <typeparam name="TInputModel10">The type of the tenth input model.</typeparam>
/// <typeparam name="TInputModel11">The type of the eleventh input model.</typeparam>
/// <typeparam name="TInputModel12">The type of the twelfth input model.</typeparam>
/// <typeparam name="TInputModel13">The type of the thirteenth input model.</typeparam>
/// <typeparam name="TInputModel14">The type of the fourteenth input model.</typeparam>
/// <typeparam name="TInputModel15">The type of the fifteenth input model.</typeparam>
/// <typeparam name="TInputModel16">The type of the sixteenth input model.</typeparam>
/// <typeparam name="TInputModel17">The type of the seventeenth input model.</typeparam>
/// <typeparam name="TInputModel18">The type of the eighteenth input model.</typeparam>
/// <typeparam name="TInputModel19">The type of the nineteenth input model.</typeparam>
/// <typeparam name="TInputModel20">The type of the twentieth input model.</typeparam>
/// <typeparam name="TInputModel21">The type of the twenty-first input model.</typeparam>
/// <typeparam name="TInputModel22">The type of the twenty-second input model.</typeparam>
/// <typeparam name="TInputModel23">The type of the twenty-third input model.</typeparam>
/// <typeparam name="TInputModel24">The type of the twenty-fourth input model.</typeparam>
public abstract class ConsoleApplication<
    TInputModel1,
    TInputModel2,
    TInputModel3,
    TInputModel4,
    TInputModel5,
    TInputModel6,
    TInputModel7,
    TInputModel8,
    TInputModel9,
    TInputModel10,
    TInputModel11,
    TInputModel12,
    TInputModel13,
    TInputModel14,
    TInputModel15,
    TInputModel16,
    TInputModel17,
    TInputModel18,
    TInputModel19,
    TInputModel20,
    TInputModel21,
    TInputModel22,
    TInputModel23,
    TInputModel24
>(IConsoleLogger logger)
    : ConsoleApplication<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9,
        TInputModel10,
        TInputModel11,
        TInputModel12,
        TInputModel13,
        TInputModel14,
        TInputModel15,
        TInputModel16,
        TInputModel17,
        TInputModel18,
        TInputModel19,
        TInputModel20,
        TInputModel21,
        TInputModel22,
        TInputModel23
    >(logger),
        IConsoleApplication<
            TInputModel1,
            TInputModel2,
            TInputModel3,
            TInputModel4,
            TInputModel5,
            TInputModel6,
            TInputModel7,
            TInputModel8,
            TInputModel9,
            TInputModel10,
            TInputModel11,
            TInputModel12,
            TInputModel13,
            TInputModel14,
            TInputModel15,
            TInputModel16,
            TInputModel17,
            TInputModel18,
            TInputModel19,
            TInputModel20,
            TInputModel21,
            TInputModel22,
            TInputModel23,
            TInputModel24
        >
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
    where TInputModel3 : class, IConsoleCommandInputModel
    where TInputModel4 : class, IConsoleCommandInputModel
    where TInputModel5 : class, IConsoleCommandInputModel
    where TInputModel6 : class, IConsoleCommandInputModel
    where TInputModel7 : class, IConsoleCommandInputModel
    where TInputModel8 : class, IConsoleCommandInputModel
    where TInputModel9 : class, IConsoleCommandInputModel
    where TInputModel10 : class, IConsoleCommandInputModel
    where TInputModel11 : class, IConsoleCommandInputModel
    where TInputModel12 : class, IConsoleCommandInputModel
    where TInputModel13 : class, IConsoleCommandInputModel
    where TInputModel14 : class, IConsoleCommandInputModel
    where TInputModel15 : class, IConsoleCommandInputModel
    where TInputModel16 : class, IConsoleCommandInputModel
    where TInputModel17 : class, IConsoleCommandInputModel
    where TInputModel18 : class, IConsoleCommandInputModel
    where TInputModel19 : class, IConsoleCommandInputModel
    where TInputModel20 : class, IConsoleCommandInputModel
    where TInputModel21 : class, IConsoleCommandInputModel
    where TInputModel22 : class, IConsoleCommandInputModel
    where TInputModel23 : class, IConsoleCommandInputModel
    where TInputModel24 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel24" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel24" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public abstract Task RunAsync(TInputModel24 input, CancellationToken stoppingToken = default);
}
