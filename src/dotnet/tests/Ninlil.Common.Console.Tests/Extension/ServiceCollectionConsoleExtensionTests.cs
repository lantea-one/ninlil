using Microsoft.Extensions.DependencyInjection;
using Ninlil.Common.Console;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Extension;
using Ninlil.Common.Console.Model;

// Define our namespace for the service-collection console extension tests.
namespace Ninlil.Common.Console.Tests.Extension;

/// <summary>
///     This test class exercises <see cref="ServiceCollectionConsoleExtension" /> - every
///     <c>UseConsoleLogging</c> overload's dependency-injection registration, including a real,
///     significant bug fixed in this project: the class-typed
///     <c>UseConsoleLogging&lt;TClassPrefix&gt;(format, prefix)</c> overload silently registered under
///     the plain, non-generic <see cref="IConsoleLogger" /> service type instead of
///     <see cref="IConsoleLogger{TClassPrefix}" />, because it built the singleton from a factory
///     delegate whose inferred return type came from <see cref="ConsoleLogger{TClass}.Create(ConsoleLoggerLogFormatEnum, IEnumerable{string})" />'s
///     own (at the time incorrectly declared) return type. Any consumer resolving
///     <see cref="IConsoleLogger{TClassPrefix}" /> from the container would have failed with a
///     missing-registration error - see <see cref="ConsoleLoggerTests.GenericCreate_WithFormatAndPrefix_ReturnsGenericallyTypedInstance" />
///     for the narrower, non-DI regression test of the same root cause.
/// </summary>
public class ServiceCollectionConsoleExtensionTests
{
    /// <summary>Verifies that <see cref="ServiceCollectionConsoleExtension.UseConsoleLogging(IServiceCollection)" /> registers a resolvable <see cref="IConsoleLogger" />.</summary>
    [Fact]
    public void UseConsoleLogging_Parameterless_RegistersConsoleLogger()
    {
        ServiceCollection services = new();

        services.UseConsoleLogging();

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<IConsoleLogger>());
    }

    /// <summary>Verifies that <see cref="ServiceCollectionConsoleExtension.UseConsoleLogging(IServiceCollection)" /> doesn't register a duplicate singleton if called twice.</summary>
    [Fact]
    public void UseConsoleLogging_CalledTwice_DoesNotDuplicateRegistration()
    {
        ServiceCollection services = new();

        services.UseConsoleLogging();
        services.UseConsoleLogging();

        Assert.Single(services, d => d.ServiceType == typeof(IConsoleLogger));
    }

    /// <summary>Verifies that <see cref="ServiceCollectionConsoleExtension.UseConsoleLogging(IServiceCollection, IConsoleLoggerConfigurationModel)" /> registers a logger using the supplied configuration.</summary>
    [Fact]
    public void UseConsoleLogging_WithConfiguration_RegistersConfiguredLogger()
    {
        ServiceCollection services = new();
        ConsoleLoggerConfigurationModel configuration = new()
        {
            Format = ConsoleLoggerLogFormatEnum.Xml,
        };

        services.UseConsoleLogging(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        IConsoleLogger logger = provider.GetRequiredService<IConsoleLogger>();
        Assert.Same(configuration, logger.Configuration);
    }

    /// <summary>Verifies that <see cref="ServiceCollectionConsoleExtension.UseConsoleLogging(IServiceCollection, ConsoleLoggerLogFormatEnum, IEnumerable{string})" /> registers a logger with the supplied format.</summary>
    [Fact]
    public void UseConsoleLogging_WithFormat_RegistersLoggerWithFormat()
    {
        ServiceCollection services = new();

        services.UseConsoleLogging(ConsoleLoggerLogFormatEnum.Json, ["svc"]);

        using ServiceProvider provider = services.BuildServiceProvider();
        IConsoleLogger logger = provider.GetRequiredService<IConsoleLogger>();
        Assert.Equal(ConsoleLoggerLogFormatEnum.Json, logger.Configuration.Format);
    }

    /// <summary>Verifies that <see cref="ServiceCollectionConsoleExtension.UseConsoleLogging{TClassPrefix}(IServiceCollection)" /> registers under the class-typed <see cref="IConsoleLogger{TClass}" /> service type.</summary>
    [Fact]
    public void UseConsoleLoggingGeneric_Parameterless_RegistersUnderGenericServiceType()
    {
        ServiceCollection services = new();

        services.UseConsoleLogging<ServiceCollectionConsoleExtensionTests>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IConsoleLogger<ServiceCollectionConsoleExtensionTests> logger = provider.GetRequiredService<
            IConsoleLogger<ServiceCollectionConsoleExtensionTests>
        >();
        Assert.NotNull(logger);
    }

    /// <summary>Verifies that <see cref="ServiceCollectionConsoleExtension.UseConsoleLogging{TClassPrefix}(IServiceCollection, IConsoleLoggerConfigurationModel)" /> registers under the class-typed service type.</summary>
    [Fact]
    public void UseConsoleLoggingGeneric_WithConfiguration_RegistersUnderGenericServiceType()
    {
        ServiceCollection services = new();
        ConsoleLoggerConfigurationModel configuration = new();

        services.UseConsoleLogging<ServiceCollectionConsoleExtensionTests>(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.NotNull(
            provider.GetService<IConsoleLogger<ServiceCollectionConsoleExtensionTests>>()
        );
    }

    /// <summary>
    ///     Verifies that <see cref="ServiceCollectionConsoleExtension.UseConsoleLogging{TClassPrefix}(IServiceCollection, ConsoleLoggerLogFormatEnum, IEnumerable{string})" />
    ///     registers under the class-typed <see cref="IConsoleLogger{TClass}" /> service type - the
    ///     exact real bug described in this class's own summary, fixed by correcting
    ///     <see cref="ConsoleLogger{TClass}.Create(ConsoleLoggerLogFormatEnum, IEnumerable{string})" />'s
    ///     return type. Before the fix, this registration was silently made under the plain
    ///     <see cref="IConsoleLogger" /> service type instead, so resolving the class-typed service
    ///     here would have returned null.
    /// </summary>
    [Fact]
    public void UseConsoleLoggingGeneric_WithFormat_RegistersUnderGenericServiceType()
    {
        ServiceCollection services = new();

        services.UseConsoleLogging<ServiceCollectionConsoleExtensionTests>(
            ConsoleLoggerLogFormatEnum.Yaml,
            ["svc"]
        );

        using ServiceProvider provider = services.BuildServiceProvider();
        IConsoleLogger<ServiceCollectionConsoleExtensionTests> logger = provider.GetRequiredService<
            IConsoleLogger<ServiceCollectionConsoleExtensionTests>
        >();

        Assert.Equal(ConsoleLoggerLogFormatEnum.Yaml, logger.Configuration.Format);
        Assert.Contains(
            nameof(ServiceCollectionConsoleExtensionTests),
            logger.Configuration.Prefix
        );
    }

    /// <summary>
    ///     Verifies that the class-typed and plain <see cref="IConsoleLogger" /> registrations are
    ///     independent - registering one does not satisfy a resolution request for the other.
    /// </summary>
    [Fact]
    public void UseConsoleLoggingGeneric_DoesNotSatisfyPlainConsoleLoggerResolution()
    {
        ServiceCollection services = new();

        services.UseConsoleLogging<ServiceCollectionConsoleExtensionTests>(
            ConsoleLoggerLogFormatEnum.Json
        );

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IConsoleLogger>());
        Assert.NotNull(
            provider.GetService<IConsoleLogger<ServiceCollectionConsoleExtensionTests>>()
        );
    }
}
