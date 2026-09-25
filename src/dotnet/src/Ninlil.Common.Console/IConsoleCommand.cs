using Microsoft.Extensions.DependencyInjection;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console;

/// <summary>
///     This interface is the contract for <see cref="ConsoleCommand" />, the command-line parser and
///     runner. It registers services, sets the not-parsed handler, and parses the arguments into
///     one of up to 24 candidate input model types before handing the result to a delegate or an
///     <see cref="IConsoleApplication" />.
/// </summary>
public interface IConsoleCommand
{
    /// <summary>
    ///     This delegate handles the case where no input model matches the command-line arguments.
    ///     It is synchronous despite its name.
    /// </summary>
    /// <typeparam name="TException">The type of the exception that occurred during parsing.</typeparam>
    public delegate void CommandNotParsedAsyncDelegate<in TException>(TException exception)
        where TException : Exception;

    /// <summary>
    ///     This delegate receives the parsed input model and the service provider built for the run.
    /// </summary>
    /// <param name="input">The input model containing the parsed command arguments.</param>
    /// <param name="serviceProvider">The service provider to resolve dependencies.</param>
    /// <param name="stoppingToken">An optional cancellation token for asynchronous operations.</param>
    /// <typeparam name="TInputModel">The type of the input model containing the parsed command arguments.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsed command has been processed.</returns>
    public delegate Task CommandParsedAsyncDelegate<in TInputModel>(
        TInputModel input,
        IServiceProvider serviceProvider,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method registers the <typeparamref name="TImplementation" /> service and optional
    ///     <paramref name="factory" /> as a scoped service in the dependency injection container.
    /// </summary>
    /// <param name="factory">The optional factory to use for creating the service.</param>
    /// <typeparam name="TImplementation">The type of the implementation to register.</typeparam>
    /// <returns>The <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand AddScopedService<TImplementation>(
        Func<IServiceProvider, TImplementation> factory = null
    )
        where TImplementation : class;

    /// <summary>
    ///     This method registers the <typeparamref name="TInterface" /> and <typeparamref name="TImplementation" />
    ///     service and optional <paramref name="factory" /> as a scoped service in the dependency injection container.
    /// </summary>
    /// <param name="factory">The optional factory to use for creating the service.</param>
    /// <typeparam name="TInterface">The type of the interface to register.</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation to register.</typeparam>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand AddScopedService<TInterface, TImplementation>(
        Func<IServiceProvider, TImplementation> factory = null
    )
        where TInterface : class
        where TImplementation : class, TInterface;

    /// <summary>
    ///     This method registers the <typeparamref name="TImplementation" /> service and optional
    ///     <paramref name="factory" /> as a singleton service in the dependency injection container.
    /// </summary>
    /// <param name="factory">The optional factory to use for creating the service.</param>
    /// <typeparam name="TImplementation">The type of the implementation to register.</typeparam>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand AddSingletonService<TImplementation>(
        Func<IServiceProvider, TImplementation> factory = null
    )
        where TImplementation : class;

    /// <summary>
    ///     This method registers the <typeparamref name="TInterface" /> and <typeparamref name="TImplementation" />
    ///     service and optional <paramref name="factory" /> as a singleton service in the dependency injection container.
    /// </summary>
    /// <param name="factory">The optional factory to use for creating the service.</param>
    /// <typeparam name="TInterface">The type of the interface to register.</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation to register.</typeparam>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand AddSingletonService<TInterface, TImplementation>(
        Func<IServiceProvider, TImplementation> factory = null
    )
        where TInterface : class
        where TImplementation : class, TInterface;

    /// <summary>
    ///     This method registers the <typeparamref name="TImplementation" /> service and optional
    ///     <paramref name="factory" /> as a transient service in the dependency injection container.
    /// </summary>
    /// <param name="factory">The optional factory to use for creating the service.</param>
    /// <typeparam name="TImplementation">The type of the implementation to register.</typeparam>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand AddTransientService<TImplementation>(
        Func<IServiceProvider, TImplementation> factory = null
    )
        where TImplementation : class;

    /// <summary>
    ///     This method registers the <typeparamref name="TInterface" /> and <typeparamref name="TImplementation" />
    ///     service and optional <paramref name="factory" /> as a transient service in the dependency injection container.
    /// </summary>
    /// <param name="factory">The optional factory to use for creating the service.</param>
    /// <typeparam name="TInterface">The type of the interface to register.</typeparam>
    /// <typeparam name="TImplementation">The type of the implementation to register.</typeparam>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand AddTransientService<TInterface, TImplementation>(
        Func<IServiceProvider, TImplementation> factory = null
    )
        where TInterface : class
        where TImplementation : class, TInterface;

    /// <summary>
    ///     This method asynchronously executes a console application of the specified <paramref name="consoleApplicationType" />.
    /// </summary>
    /// <param name="consoleApplicationType">The type of the console application to execute.</param>
    /// <param name="stoppingToken">Optional cancellation token to stop the execution.</param>
    /// <exception cref="ArgumentException">
    ///     When the provided <paramref name="consoleApplicationType" /> is not a valid <see cref="IConsoleApplication" />.
    /// </exception>
    public Task ApplicationAsync(
        Type consoleApplicationType,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method asynchronously executes a <see cref="IConsoleApplication" />
    ///     after parsing the command-line arguments.
    /// </summary>
    /// <param name="stoppingToken">Optional cancellation token to stop the execution.</param>
    /// <typeparam name="TConsoleApplication">The type of the console application to execute.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the execution is complete.</returns>
    public Task ApplicationAsync<TConsoleApplication>(CancellationToken stoppingToken = default)
        where TConsoleApplication : class, IConsoleApplication;

    /// <summary>
    ///     This method lets <paramref name="handler" /> register services in the service collection
    ///     used for each parse.
    /// </summary>
    /// <param name="handler">The handler to configure the services.</param>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand ConfigureServices(Action<IServiceCollection> handler);

    /// <summary>
    ///     This method resolves a registered service from the instance's
    ///     <see cref="IServiceCollection" />.
    /// </summary>
    /// <typeparam name="TService">The type of the service to retrieve.</typeparam>
    /// <returns>The service instance of the specified type <typeparamref name="TService" />.</returns>
    public TService GetService<TService>()
        where TService : class;

    /// <summary>
    ///     This method registers a handler to run when no input model matches the arguments.
    /// </summary>
    /// <param name="handler">
    ///     The handler to execute when the command-line arguments are not parsed.
    /// </param>
    /// <typeparam name="TException">The type of the exception that occurred during parsing.</typeparam>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand WithNotParsed<TException>(
        CommandNotParsedAsyncDelegate<TException> handler
    )
        where TException : Exception;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of the <typeparamref name="TInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{TInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel">The type of the input model to parse.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<TInputModel>(
        CommandParsedAsyncDelegate<TInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<TInputModel1, TInputModel2>(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<TInputModel1, TInputModel2, TInputModel3>(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<TInputModel1, TInputModel2, TInputModel3, TInputModel4>(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel
        where TInputModel9 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel
        where TInputModel9 : class, IConsoleCommandInputModel
        where TInputModel10 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel11 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel12 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel13 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel14 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel15 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel16 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel17 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel18 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel19 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel20 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel21 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel22 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel23">The type of the twenty-third input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel23 : class, IConsoleCommandInputModel;

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of one of the
    ///     provided type parameter <see cref="IConsoleCommandInputModel" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     used to handle the parsed arguments from the command-line.
    /// </param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel1">The type of the first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel23">The type of the twenty-third input model that can be handled.</typeparam>
    /// <typeparam name="TInputModel24">The type of the twenty-fourth input model that can be handled.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    public Task WithParsedAsync<
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
    >(
        CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
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
        where TInputModel24 : class, IConsoleCommandInputModel;
}
