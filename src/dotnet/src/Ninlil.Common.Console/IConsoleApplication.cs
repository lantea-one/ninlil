using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console;

/// <summary>
///     This interface maintains the structure of a console application.
/// </summary>
public interface IConsoleApplication
{
    /// <summary>
    ///     This method is responsible for executing the console application with
    ///     the parsed <paramref name="input" /> from the command line arguments.
    /// </summary>
    /// <param name="input">The parsed command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task ExecuteAsync(
        IConsoleCommandInputModel input,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method is responsible for executing the console application with the provided command line arguments.
    /// </summary>
    /// <param name="arguments">The command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task ExecuteAsync(
        IEnumerable<string> arguments,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This property contains the service provider for the console application.
    /// </summary>
    /// <param name="serviceProvider"></param>
    public void WithServiceProvider(IServiceProvider serviceProvider);
}

/// <summary>
///     This interface maintains the structure of a console application with a single input model.
/// </summary>
/// <typeparam name="TInputModel">The type of the input model.</typeparam>
public interface IConsoleApplication<in TInputModel> : IConsoleApplication
    where TInputModel : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with two input models.
/// </summary>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
public interface IConsoleApplication<in TInputModel1, in TInputModel2>
    : IConsoleApplication<TInputModel1>
    where TInputModel1 : class, IConsoleCommandInputModel
    where TInputModel2 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel2" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel2" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel2 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with three input models.
/// </summary>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
public interface IConsoleApplication<in TInputModel1, in TInputModel2, in TInputModel3>
    : IConsoleApplication<TInputModel1, TInputModel2>
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
    public Task RunAsync(TInputModel3 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with four input models.
/// </summary>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4
> : IConsoleApplication<TInputModel1, TInputModel2, TInputModel3>
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
    public Task RunAsync(TInputModel4 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with five input models.
/// </summary>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5
> : IConsoleApplication<TInputModel1, TInputModel2, TInputModel3, TInputModel4>
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
    public Task RunAsync(TInputModel5 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with six input models.
/// </summary>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6
> : IConsoleApplication<TInputModel1, TInputModel2, TInputModel3, TInputModel4, TInputModel5>
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
    public Task RunAsync(TInputModel6 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with seven input models.
/// </summary>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7
>
    : IConsoleApplication<
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
    where TInputModel7 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel7" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel7" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel7 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with eight input models.
/// </summary>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8
>
    : IConsoleApplication<
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
    where TInputModel8 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel8" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel8" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel8 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with nine input models.
/// </summary>
/// <typeparam name="TInputModel1">The type of the first input model.</typeparam>
/// <typeparam name="TInputModel2">The type of the second input model.</typeparam>
/// <typeparam name="TInputModel3">The type of the third input model.</typeparam>
/// <typeparam name="TInputModel4">The type of the fourth input model.</typeparam>
/// <typeparam name="TInputModel5">The type of the fifth input model.</typeparam>
/// <typeparam name="TInputModel6">The type of the sixth input model.</typeparam>
/// <typeparam name="TInputModel7">The type of the seventh input model.</typeparam>
/// <typeparam name="TInputModel8">The type of the eighth input model.</typeparam>
/// <typeparam name="TInputModel9">The type of the ninth input model.</typeparam>
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9
>
    : IConsoleApplication<
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
    where TInputModel9 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel9" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel9" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel9 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with ten input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10
>
    : IConsoleApplication<
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
    where TInputModel10 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel10" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel10" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel10 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with eleven input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11
>
    : IConsoleApplication<
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
    where TInputModel11 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel11" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel11" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel11 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with twelve input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12
>
    : IConsoleApplication<
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
    where TInputModel12 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel12" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel12" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel12 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with thirteen input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13
>
    : IConsoleApplication<
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
    where TInputModel13 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel13" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel13" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel13 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with fourteen input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14
>
    : IConsoleApplication<
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
    where TInputModel14 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel14" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel14" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel14 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with fifteen input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15
>
    : IConsoleApplication<
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
    where TInputModel15 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel15" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel15" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel15 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with sixteen input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16
>
    : IConsoleApplication<
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
    where TInputModel16 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel16" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel16" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel16 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with seventeen input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16,
    in TInputModel17
>
    : IConsoleApplication<
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
    where TInputModel17 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel17" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel17" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel17 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with eighteen input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16,
    in TInputModel17,
    in TInputModel18
>
    : IConsoleApplication<
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
    where TInputModel18 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel18" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel18" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel18 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with nineteen input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16,
    in TInputModel17,
    in TInputModel18,
    in TInputModel19
>
    : IConsoleApplication<
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
    where TInputModel19 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel19" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel19" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel19 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with twenty input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16,
    in TInputModel17,
    in TInputModel18,
    in TInputModel19,
    in TInputModel20
>
    : IConsoleApplication<
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
    where TInputModel20 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel20" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel20" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel20 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with twenty-one input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16,
    in TInputModel17,
    in TInputModel18,
    in TInputModel19,
    in TInputModel20,
    in TInputModel21
>
    : IConsoleApplication<
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
    where TInputModel21 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel21" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel21" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel21 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with twenty-two input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16,
    in TInputModel17,
    in TInputModel18,
    in TInputModel19,
    in TInputModel20,
    in TInputModel21,
    in TInputModel22
>
    : IConsoleApplication<
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
    where TInputModel22 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel22" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel22" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel22 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with twenty-three input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16,
    in TInputModel17,
    in TInputModel18,
    in TInputModel19,
    in TInputModel20,
    in TInputModel21,
    in TInputModel22,
    in TInputModel23
>
    : IConsoleApplication<
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
    where TInputModel23 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel23" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel23" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel23 input, CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface maintains the structure of a console application with twenty-four input models.
/// </summary>
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
public interface IConsoleApplication<
    in TInputModel1,
    in TInputModel2,
    in TInputModel3,
    in TInputModel4,
    in TInputModel5,
    in TInputModel6,
    in TInputModel7,
    in TInputModel8,
    in TInputModel9,
    in TInputModel10,
    in TInputModel11,
    in TInputModel12,
    in TInputModel13,
    in TInputModel14,
    in TInputModel15,
    in TInputModel16,
    in TInputModel17,
    in TInputModel18,
    in TInputModel19,
    in TInputModel20,
    in TInputModel21,
    in TInputModel22,
    in TInputModel23,
    in TInputModel24
>
    : IConsoleApplication<
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
    where TInputModel24 : class, IConsoleCommandInputModel
{
    /// <summary>
    ///     This method is responsible for running the console application with the parsed <typeparamref name="TInputModel24" />.
    /// </summary>
    /// <param name="input">The parsed <typeparamref name="TInputModel24" /> from the command line arguments.</param>
    /// <param name="stoppingToken">The cancellation token to stop the operation.</param>
    /// <returns>An awaitable <see cref="Task" /> representing the result of the operation.</returns>
    public Task RunAsync(TInputModel24 input, CancellationToken stoppingToken = default);
}
