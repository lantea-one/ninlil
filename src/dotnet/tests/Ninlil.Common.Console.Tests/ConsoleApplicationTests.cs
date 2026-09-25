using Microsoft.Extensions.DependencyInjection;
using Ninlil.Common.Console;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Console.Tests;

/// <summary>
///     These tests cover <see cref="ConsoleApplication" /> and <see cref="ConsoleApplication{TInputModel}" />
///     through the <see cref="TestConsoleApplication" /> double: reflection dispatch from an input
///     model to the matching <c>RunAsync</c> overload, service lookup, and the metadata setters that
///     change <see cref="ConsoleCommand" />'s static, process-wide fields.
/// </summary>
public class ConsoleApplicationTests
{
    /// <summary>
    ///     Verifies that <see cref="ConsoleApplication.ExecuteAsync(IConsoleCommandInputModel, CancellationToken)" />
    ///     finds and invokes the <c>RunAsync</c> overload for the input's type via reflection.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithMatchingInputModel_DispatchesToRunAsync()
    {
        TestConsoleApplication app = new();
        SimpleNamedArgsModel input = new() { Name = "John" };

        await app.ExecuteAsync(input);

        Assert.Same(input, app.ReceivedInput);
        Assert.Equal(1, app.RunCount);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleApplication.ExecuteAsync(IConsoleCommandInputModel, CancellationToken)" />
    ///     throws <see cref="NotSupportedException" /> for an input model type with no matching
    ///     <c>RunAsync</c> overload.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithMismatchedInputModel_ThrowsNotSupportedException()
    {
        TestConsoleApplication app = new();
        PositionalArgsModel input = new();

        await Assert.ThrowsAsync<NotSupportedException>(() => app.ExecuteAsync(input));
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleApplication.WithServiceProvider" /> makes a registered service
    ///     resolvable through the protected <see cref="ConsoleApplication.GetService{TService}()" /> helper.
    /// </summary>
    [Fact]
    public void WithServiceProvider_MakesServiceResolvable()
    {
        TestConsoleApplication app = new();
        ServiceCollection services = new();
        services.AddSingleton("registered-value");
        app.WithServiceProvider(services.BuildServiceProvider());

        string result = app.ResolveService<string>();

        Assert.Equal("registered-value", result);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleApplication.WithApplication" />/<c>WithAuthor</c>/<c>WithCompany</c>/
    ///     <c>WithDescription</c>/<c>WithVersion</c> update <see cref="ConsoleCommand" />'s static
    ///     metadata fields. The original values are saved and restored because the fields are static
    ///     and shared with every other test in the process.
    /// </summary>
    [Fact]
    public void WithMetadataSetters_UpdateConsoleCommandStaticFields()
    {
        string originalApplication = ConsoleCommand.Application;
        string originalAuthor = ConsoleCommand.Author;
        string originalCompany = ConsoleCommand.Company;
        string originalDescription = ConsoleCommand.Description;
        string originalVersion = ConsoleCommand.Version;

        try
        {
            TestConsoleApplication app = new();

            app.WithApplication("MyApp");
            app.WithAuthor("Jane Doe");
            app.WithCompany("Acme");
            app.WithDescription("Does things.");
            app.WithVersion("2.0.0");

            Assert.Equal("MyApp", ConsoleCommand.Application);
            Assert.Equal("Jane Doe", ConsoleCommand.Author);
            Assert.Equal("Acme", ConsoleCommand.Company);
            Assert.Equal("Does things.", ConsoleCommand.Description);
            Assert.Equal("2.0.0", ConsoleCommand.Version);
        }
        finally
        {
            ConsoleCommand.Application = originalApplication;
            ConsoleCommand.Author = originalAuthor;
            ConsoleCommand.Company = originalCompany;
            ConsoleCommand.Description = originalDescription;
            ConsoleCommand.Version = originalVersion;
        }
    }
}
