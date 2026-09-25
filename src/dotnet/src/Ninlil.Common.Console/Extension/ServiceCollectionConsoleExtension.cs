using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Extension;

/// <summary>
///     This static class registers the console logger with dependency injection. The
///     non-generic overloads register an <see cref="IConsoleLogger" /> singleton; the generic ones
///     register an <see cref="IConsoleLogger{TClass}" /> whose prefix is the class name. Each
///     overload uses <c>TryAdd</c>, so an existing registration is kept.
/// </summary>
public static class ServiceCollectionConsoleExtension
{
    /// <summary>
    ///     This extension method adds the console logger singleton
    ///     to the service collection if it doesn't already exist.
    /// </summary>
    /// <param name="services">
    ///     The <see cref="IServiceCollection" /> instance to which the console logger singleton will be added.
    /// </param>
    /// <returns>
    ///     The updated <paramref name="services" /> instance with the console logger singleton added.
    /// </returns>
    public static IServiceCollection UseConsoleLogging(this IServiceCollection services)
    {
        // Add the console logger singleton to the services if it doesn't already exist.
        services.TryAddSingleton(_ => ConsoleLogger.Create());

        // We're done, return the updated service collection.
        return services;
    }

    /// <summary>
    ///     This extension method adds the console logger singleton
    ///     to the service collection if it doesn't already exist
    ///     with a custom <paramref name="configuration" /> model.
    /// </summary>
    /// <param name="services">
    ///     The <see cref="IServiceCollection" /> instance to which the console logger singleton will be added.
    /// </param>
    /// <param name="configuration">
    ///     The <see cref="IConsoleLoggerConfigurationModel" /> instance
    ///     that defines the configuration for the console logger.
    /// </param>
    /// <returns>
    ///     The updated <paramref name="services" /> instance with the console logger singleton added.
    /// </returns>
    public static IServiceCollection UseConsoleLogging(
        this IServiceCollection services,
        IConsoleLoggerConfigurationModel configuration
    )
    {
        // Add the console logger singleton to the services if it doesn't already exist.
        services.TryAddSingleton(_ => ConsoleLogger.Create(configuration));

        // We're done, return the updated service collection.
        return services;
    }

    /// <summary>
    ///     This extension method adds the console logger singleton
    ///     to the service collection if it doesn't already exist
    ///     with a custom log <paramref name="format" /> and
    ///     optional <paramref name="prefix" /> for log messages.
    /// </summary>
    /// <param name="services">
    ///     The <see cref="IServiceCollection" /> instance to which the console logger singleton will be added.
    /// </param>
    /// <param name="format">
    ///     The <see cref="ConsoleLoggerLogFormatEnum" /> value that defines the log format for the console logger.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that will be used as prefixes for log messages.
    /// </param>
    /// <returns>
    ///     The updated <paramref name="services" /> instance with the console logger singleton added.
    /// </returns>
    public static IServiceCollection UseConsoleLogging(
        this IServiceCollection services,
        ConsoleLoggerLogFormatEnum format,
        IEnumerable<string> prefix = null
    )
    {
        // Add the console logger singleton to the services if it doesn't already exist.
        services.TryAddSingleton(_ => ConsoleLogger.Create(format, prefix));

        // We're done, return the updated service collection.
        return services;
    }

    /// <summary>
    ///     This extension method adds the console logger singleton
    ///     to the service collection if it doesn't already exist.
    /// </summary>
    /// <param name="services">
    ///     The <see cref="IServiceCollection" /> instance to which the console logger singleton will be added.
    /// </param>
    /// <typeparam name="TClassPrefix">The type whose name will be used as a prefix for log messages.</typeparam>
    /// <returns>
    ///     The updated <paramref name="services" /> instance with the console logger singleton added.
    /// </returns>
    public static IServiceCollection UseConsoleLogging<TClassPrefix>(
        this IServiceCollection services
    )
        where TClassPrefix : class
    {
        // Add the console logger singleton to the services if it doesn't already exist.
        services.TryAddSingleton(_ => ConsoleLogger<TClassPrefix>.Create());

        // We're done, return the updated service collection.
        return services;
    }

    /// <summary>
    ///     This extension method adds the console logger singleton
    ///     to the service collection if it doesn't already exist
    ///     with a custom <paramref name="configuration" /> model.
    /// </summary>
    /// <param name="services">
    ///     The <see cref="IServiceCollection" /> instance to which the console logger singleton will be added.
    /// </param>
    /// <param name="configuration">
    ///     The <see cref="IConsoleLoggerConfigurationModel" /> instance
    ///     that defines the configuration for the console logger.
    /// </param>
    /// <typeparam name="TClassPrefix">The type whose name will be used as a prefix for log messages.</typeparam>
    /// <returns>
    ///     The updated <paramref name="services" /> instance with the console logger singleton added.
    /// </returns>
    public static IServiceCollection UseConsoleLogging<TClassPrefix>(
        this IServiceCollection services,
        IConsoleLoggerConfigurationModel configuration
    )
        where TClassPrefix : class
    {
        // Add the console logger singleton to the services if it doesn't already exist.
        services.TryAddSingleton(_ => ConsoleLogger<TClassPrefix>.Create(configuration));

        // We're done, return the updated service collection.
        return services;
    }

    /// <summary>
    ///     This extension method adds the console logger singleton
    ///     to the service collection if it doesn't already exist
    ///     with a custom log <paramref name="format" /> and
    ///     optional <paramref name="prefix" /> for log messages.
    /// </summary>
    /// <param name="services">
    ///     The <see cref="IServiceCollection" /> instance to which the console logger singleton will be added.
    /// </param>
    /// <param name="format">
    ///     The <see cref="ConsoleLoggerLogFormatEnum" /> value that defines the log format for the console logger.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that will be used as prefixes for log messages.
    /// </param>
    /// <typeparam name="TClassPrefix">The type whose name will be used as a prefix for log messages.</typeparam>
    /// <returns>
    ///     The updated <paramref name="services" /> instance with the console logger singleton added.
    /// </returns>
    public static IServiceCollection UseConsoleLogging<TClassPrefix>(
        this IServiceCollection services,
        ConsoleLoggerLogFormatEnum format,
        IEnumerable<string> prefix = null
    )
        where TClassPrefix : class
    {
        // Add the console logger singleton to the services if it doesn't already exist.
        services.TryAddSingleton(_ => ConsoleLogger<TClassPrefix>.Create(format, prefix));

        // We're done, return the updated service collection.
        return services;
    }
}
