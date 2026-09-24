using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Model;
using Ninlil.Common.Console.Service;

// Define our namespace.
namespace Ninlil.Common.Console;

/// <summary>
///     This class represents a console command that can be used to execute various operations through the CLI.
/// </summary>
/// <param name="arguments">The CLI arguments passed to the console command.</param>
public class ConsoleCommand(IEnumerable<string> arguments) : IConsoleCommand
{
    /// <summary>
    ///     This property is the internal container for the name of the application.
    /// </summary>
    private static string _application = (
        Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly()
    )
        .GetName()
        .Name;

    /// <summary>
    ///     This property is the internal container for the author of the application.
    /// </summary>
    private static string _author;

    /// <summary>
    ///     This property is the internal container for the company that produced the application.
    /// </summary>
    private static string _company = (Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly())
        .GetCustomAttribute<AssemblyCompanyAttribute>()
        ?.Company;

    /// <summary>
    ///     This property is the internal container for the description of the application.
    /// </summary>
    private static string _description = (
        Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly()
    )
        .GetCustomAttribute<AssemblyDescriptionAttribute>()
        ?.Description;

    /// <summary>
    ///     This property is the internal container for the version of the application.
    /// </summary>
    private static string _version = (Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly())
        .GetName()
        .Version?.ToString();

    /// <summary>
    ///     This property contains the name of the application.
    /// </summary>
    public static string Application
    {
        get => _application?.Trim();
        set => _application = value?.Trim();
    }

    /// <summary>
    ///     This property contains the author of the application.
    /// </summary>
    public static string Author
    {
        get => _author?.Trim();
        set => _author = value?.Trim();
    }

    /// <summary>
    ///     This property contains the company that produced the application.
    /// </summary>
    public static string Company
    {
        get => _company?.Trim();
        set => _company = value?.Trim();
    }

    /// <summary>
    ///     This property contains the description of the application.
    /// </summary>
    public static string Description
    {
        get => _description?.Trim();
        set => _description = value?.Trim();
    }

    /// <summary>
    ///     This property contains the help request switches that can be used to request help information.
    /// </summary>
    public static readonly string[] HelpRequestSwitches = ["?", "/?", "/h", "h", "help", "version"];

    /// <summary>
    ///     This property contains the version of the application.
    /// </summary>
    public static string Version
    {
        get => _version?.Trim();
        set => _version = value?.Trim();
    }

    /// <summary>
    ///     This field contains the default <see cref="ConsoleCommand" /> instance.
    /// </summary>
    /// <param name="arguments">The CLI arguments passed to the console command.</param>
    public static ConsoleCommand Create(IEnumerable<string> arguments) => new(arguments);

    /// <summary>
    ///     This method returns the help text for the given <paramref name="types" />.
    /// </summary>
    /// <param name="types">The types for which to get the help text.</param>
    /// <returns>A string containing the help text for the specified types.</returns>
    public static string GetHelpText(params Type[] types)
    {
        // Define our help-text collection.
        ConsoleHelpTextCollectionModel helpText = new ConsoleHelpTextCollectionModel()
            .WithApplication(Application)
            .WithAuthor(Author)
            .WithCompany(Company)
            .WithDescription(Description)
            .WithVersion(Version);

        // Instantiate our help text string builder instance.
        StringBuilder builder = helpText.WriteStartHelpText();

        // Iterate over the types, updating the help text collection.
        foreach (Type type in types.Distinct())
            ConsoleCommandInputModel.GetHelpTextCollection(type).ToStringBuilder(builder, true);

        // We're done, build and return the help text.
        return builder.AppendLine().ToString().Trim();
    }

    /// <summary>
    ///     This method returns the help text for the given <typeparamref name="TInputModel1" />.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public static string GetHelpText<TInputModel1>()
        where TInputModel1 : class, IConsoleCommandInputModel => GetHelpText(typeof(TInputModel1));

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public static string GetHelpText<TInputModel1, TInputModel2>()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel =>
        GetHelpText(typeof(TInputModel1), typeof(TInputModel2));

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public static string GetHelpText<TInputModel1, TInputModel2, TInputModel3>()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel =>
        GetHelpText(typeof(TInputModel1), typeof(TInputModel2), typeof(TInputModel3));

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public static string GetHelpText<TInputModel1, TInputModel2, TInputModel3, TInputModel4>()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public static string GetHelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public static string GetHelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel
        where TInputModel9 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
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
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel
        where TInputModel9 : class, IConsoleCommandInputModel
        where TInputModel10 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
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
    >()
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
        where TInputModel11 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
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
    >()
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
        where TInputModel12 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
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
    >()
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
        where TInputModel13 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
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
    >()
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
        where TInputModel14 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
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
    >()
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
        where TInputModel15 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model type.</returns>
    public string GetHelpText<
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
    >()
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
        where TInputModel16 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model types.</returns>
    public static string GetHelpText<
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
    >()
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
        where TInputModel17 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model types.</returns>
    public static string GetHelpText<
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
    >()
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
        where TInputModel18 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model types.</returns>
    public static string GetHelpText<
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
    >()
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
        where TInputModel19 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model types.</returns>
    public static string GetHelpText<
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
    >()
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
        where TInputModel20 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model types.</returns>
    public static string GetHelpText<
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
    >()
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
        where TInputModel21 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model types.</returns>
    public static string GetHelpText<
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
    >()
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
        where TInputModel22 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel23">The type of the twenty-third input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model types.</returns>
    public static string GetHelpText<
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
    >()
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
        where TInputModel23 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22),
            typeof(TInputModel23)
        );

    /// <summary>
    ///     This method returns the help text for the given type parameters.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel23">The type of the twenty-third input model for which to get the help text.</typeparam>
    /// <typeparam name="TInputModel24">The type of the twenty-fourth input model for which to get the help text.</typeparam>
    /// <returns>The help text for the specified input model types.</returns>
    public static string GetHelpText<
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
    >()
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
        where TInputModel24 : class, IConsoleCommandInputModel =>
        GetHelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22),
            typeof(TInputModel23),
            typeof(TInputModel24)
        );

    /// <summary>
    ///     This method prints the help text for the given <paramref name="types" /> to the console.
    /// </summary>
    /// <param name="types">The types for which to print the help text.</param>
    public static void HelpText(params Type[] types) =>
        ConsoleService.WriteLine(ConsoleColor.DarkCyan, GetHelpText(types));

    /// <summary>
    ///     This method prints the help text for the given <typeparamref name="TInputModel1" /> to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the input model for which to print the help text.</typeparam>
    public static void HelpText<TInputModel1>()
        where TInputModel1 : class, IConsoleCommandInputModel => HelpText(typeof(TInputModel1));

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    public static void HelpText<TInputModel1, TInputModel2>()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel =>
        HelpText(typeof(TInputModel1), typeof(TInputModel2));

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    public static void HelpText<TInputModel1, TInputModel2, TInputModel3>()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel =>
        HelpText(typeof(TInputModel1), typeof(TInputModel2), typeof(TInputModel3));

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    public static void HelpText<TInputModel1, TInputModel2, TInputModel3, TInputModel4>()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    public static void HelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    public static void HelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    public static void HelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    public static void HelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    public static void HelpText<
        TInputModel1,
        TInputModel2,
        TInputModel3,
        TInputModel4,
        TInputModel5,
        TInputModel6,
        TInputModel7,
        TInputModel8,
        TInputModel9
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel
        where TInputModel9 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel
        where TInputModel9 : class, IConsoleCommandInputModel
        where TInputModel10 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel11 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel12 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel13 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel14 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel15 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel16 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel17 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel18 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel19 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel20 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel21 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel22 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel23">The type of the twenty-third input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel23 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22),
            typeof(TInputModel23)
        );

    /// <summary>
    ///     This method prints the help text for the given type parameters to the console.
    /// </summary>
    /// <typeparam name="TInputModel1">The type of the first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel2">The type of the second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel3">The type of the third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel4">The type of the fourth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel5">The type of the fifth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel6">The type of the sixth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel7">The type of the seventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel8">The type of the eighth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel9">The type of the ninth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel10">The type of the tenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel11">The type of the eleventh input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel12">The type of the twelfth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel13">The type of the thirteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel14">The type of the fourteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel15">The type of the fifteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel16">The type of the sixteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel17">The type of the seventeenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel18">The type of the eighteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel19">The type of the nineteenth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel20">The type of the twentieth input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel21">The type of the twenty-first input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel22">The type of the twenty-second input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel23">The type of the twenty-third input model for which to print the help text.</typeparam>
    /// <typeparam name="TInputModel24">The type of the twenty-fourth input model for which to print the help text.</typeparam>
    public static void HelpText<
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
    >()
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
        where TInputModel24 : class, IConsoleCommandInputModel =>
        HelpText(
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22),
            typeof(TInputModel23),
            typeof(TInputModel24)
        );

    /// <summary>
    ///     This property contains the command-line arguments to parse.
    /// </summary>
    private readonly IEnumerable<string> _arguments = arguments;

    /// <summary>
    ///     This property contains the handler for when the command-line arguments are not parsed.
    /// </summary>
    private readonly Dictionary<
        Type,
        IConsoleCommand.CommandNotParsedAsyncDelegate<Exception>
    > _notParsed = new([
        new KeyValuePair<Type, IConsoleCommand.CommandNotParsedAsyncDelegate<Exception>>(
            typeof(Exception),
            e => ConsoleService.WriteLine(e, true)
        ),
    ]);

    /// <summary>
    ///     This property contains our service collection for dependency injection.
    /// </summary>
    private readonly IServiceCollection _services = new ServiceCollection();

    /// <summary>
    ///     This method sets up the environment and prepares it for handling the command-line arguments.
    /// </summary>
    /// <param name="input">The input model to parse.</param>
    /// <param name="handler">The delegate to invoke after the input model is parsed.</param>
    /// <param name="stoppingToken">Optional cancellation token to stop the parsing process.</param>
    /// <typeparam name="TInputModel">The type of the input model to parse.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the pre-parse processing is complete.</returns>
    private async Task BeforeHandleAsync<TInputModel>(
        TInputModel input,
        IConsoleCommand.CommandParsedAsyncDelegate<TInputModel> handler,
        CancellationToken stoppingToken = default
    )
        where TInputModel : class, IConsoleCommandInputModel
    {
        // Ensure we have a valid input model.
        if (input is null)
            return;

        // We're done, return the instance.
        await handler.Invoke(input, _services.BuildServiceProvider(), stoppingToken);
    }

    /// <summary>
    ///     This method retrieves the input model types for a <paramref name="consoleApplicationType" />.
    /// </summary>
    /// <param name="consoleApplicationType">
    ///     The type of <see cref="IConsoleApplication"/> to retrieve input model types for.
    /// </param>
    /// <returns>
    ///     The array of input model types for the specified console application type.
    /// </returns>
    private Type[] GetConsoleApplicationInputModelTypes(Type consoleApplicationType)
    {
        // Ensure we have a base type to work with.
        if (consoleApplicationType.BaseType is null)
            return Array.Empty<Type>();

        // Ensure the base type is generic.
        if (!consoleApplicationType.BaseType.IsGenericType)
            return GetConsoleApplicationInputModelTypes(consoleApplicationType.BaseType);

        // We're done, return the generic type arguments from the base type.
        return consoleApplicationType.BaseType.GetGenericArguments();
    }

    /// <summary>
    ///     This method parses the command-line arguments and returns
    ///     an instance of <see cref="IConsoleCommandInputModel" />
    ///     that matches one of the provided <paramref name="types" />.
    /// </summary>
    /// <param name="types">The various types of input models to parse.</param>
    /// <returns>The parsed input model if successful, otherwise null.</returns>
    /// <exception cref="ArgumentException">
    ///     When one or more of the provided <paramref name="types" />
    ///     are not a valid <see cref="IConsoleCommandInputModel" />.
    /// </exception>
    private TInputModel Parse<TInputModel>(params Type[] types)
        where TInputModel : class, IConsoleCommandInputModel
    {
        // Filter any bad types out of the list.
        types = types
            .Where(t => t is not null)
            .Where(t => typeof(IConsoleCommandInputModel).IsAssignableFrom(t))
            .ToArray();

        // Check for a help request.
        if (_arguments.Any())
        {
            // Check for a help-text call.
            if (
                HelpRequestSwitches.Contains(
                    NamedConsoleArgumentAttribute.SanitizeArgumentName(_arguments.First()),
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                // Print the help text for the provided types.
                HelpText(types);

                // We're done, exit the application entirely.
                Environment.Exit(0);

                // We're done, return null.
                return null;
            }

            // Try to limit the list of types to those that match the first verb of the input model.
            if (
                types.Any(t =>
                    t.GetCustomAttribute<ConsoleVerbAttribute>(false)
                        ?.Name?.Equals(_arguments.First(), StringComparison.OrdinalIgnoreCase)
                        is true
                )
            )
                types = types
                    .Where(t =>
                        t.GetCustomAttribute<ConsoleVerbAttribute>(false)
                            ?.Name?.Equals(_arguments.First(), StringComparison.OrdinalIgnoreCase)
                            is true
                    )
                    .ToArray();
        }

        // Iterate through the types and parse the command-line arguments.
        foreach (Type type in types)
        {
            // Localize the arguments into a local variable for parsing.
            string[] arguments = [];

            // Copy the arguments to the local variable.
            foreach (string a in _arguments)
                arguments = [.. arguments, a];

            // Try to parse the command-line arguments for the current type of input model.
            if (ConsoleCommandInputModel.TryParse(type, out var model, arguments))
                return model as TInputModel;
        }

        // If we get here, no valid input model was found, execute the not parsed handler(s).
        foreach (
            KeyValuePair<
                Type,
                IConsoleCommand.CommandNotParsedAsyncDelegate<Exception>
            > handler in _notParsed
        )
            handler.Value.Invoke(
                new InvalidOperationException(
                    $"No valid input model found for the provided command-line arguments from: {string.Join(", ", types.Select(t => t.Name))}"
                )
            );

        // We're done, return null.
        return null;
    }

    /// <summary>
    ///     This method parses the command-line arguments and executes the provided
    ///     <paramref name="parsedDelegate" /> upon successful parsing of the <paramref name="types" />.
    /// </summary>
    /// <param name="parsedDelegate">
    ///     The <see cref="IConsoleCommand.CommandParsedAsyncDelegate{IConsoleCommandInputModel}" />
    ///     to execute upon successful parsing.
    /// </param>
    /// <param name="stoppingToken">The cancellation token to use for stopping the parsing.</param>
    /// <param name="types">The various types of input models to parse.</param>
    /// <typeparam name="TInputModel">The type of the input model to parse.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the parsing is complete.</returns>
    private Task ParseAsync<TInputModel>(
        IConsoleCommand.CommandParsedAsyncDelegate<TInputModel> parsedDelegate,
        CancellationToken stoppingToken,
        params Type[] types
    )
        where TInputModel : class, IConsoleCommandInputModel =>
        BeforeHandleAsync(Parse<TInputModel>(types), parsedDelegate, stoppingToken);

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
        where TImplementation : class
    {
        // Check for a factory then try to add the scoped service to dependency injection.
        if (factory is null)
            _services.TryAddScoped<TImplementation>();
        // Otherwise, try to add the scoped service to dependency injection with its factory.
        else
            _services.TryAddScoped(factory);

        // We're done, return the instance.
        return this;
    }

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
        where TImplementation : class, TInterface
    {
        // Check for a factory then try to add the scoped service to dependency injection.
        if (factory is null)
            _services.TryAddScoped<TInterface, TImplementation>();
        // Otherwise, try to add the scoped service to dependency injection with its factory.
        else
            _services.AddScoped<TInterface, TImplementation>(factory);

        // We're done, return the instance.
        return this;
    }

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
        where TImplementation : class
    {
        // Check for a factory then try to add the singleton service to dependency injection.
        if (factory is null)
            _services.TryAddSingleton<TImplementation>();
        // Otherwise, try to add the singleton service to dependency injection with its factory.
        else
            _services.TryAddSingleton(factory);

        // We're done, return the instance.
        return this;
    }

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
        where TImplementation : class, TInterface
    {
        // Check for a factory then try to add the singleton service to dependency injection.
        if (factory is null)
            _services.TryAddSingleton<TInterface, TImplementation>();
        // Otherwise, try to add the singleton service to dependency injection with its factory.
        else
            _services.AddSingleton<TInterface, TImplementation>(factory);

        // We're done, return the instance.
        return this;
    }

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
        where TImplementation : class
    {
        // Check for a factory then try to add the transient service to dependency injection.
        if (factory is null)
            _services.TryAddTransient<TImplementation>();
        // Otherwise, try to add the transient service to dependency injection with its factory.
        else
            _services.TryAddTransient(factory);

        // We're done, return the instance.
        return this;
    }

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
        where TImplementation : class, TInterface
    {
        // Check for a factory then try to add the transient service to dependency injection.
        if (factory is null)
            _services.TryAddTransient<TInterface, TImplementation>();
        // Otherwise, try to add the transient service to dependency injection with its factory.
        else
            _services.AddTransient<TInterface, TImplementation>(factory);

        // We're done, return the instance.
        return this;
    }

    /// <summary>
    ///     This method asynchronously executes a console application of the specified <paramref name="consoleApplicationType" />.
    /// </summary>
    /// <param name="consoleApplicationType">The type of the console application to execute.</param>
    /// <param name="stoppingToken">Optional cancellation token to stop the execution.</param>
    /// <exception cref="ArgumentException">
    ///     When the provided <paramref name="consoleApplicationType" /> is not a valid <see cref="IConsoleApplication" />.
    /// </exception>
    public async Task ApplicationAsync(
        Type consoleApplicationType,
        CancellationToken stoppingToken = default
    )
    {
        // Check if the console application type is valid.
        if (
            consoleApplicationType is null
            || !typeof(IConsoleApplication).IsAssignableFrom(consoleApplicationType)
        )
            throw new ArgumentException(
                "The provided console application type is not valid.",
                nameof(consoleApplicationType)
            );

        // Register our console application type with the service collection.
        _services.AddSingleton(consoleApplicationType);

        // Localize the type of the input model for the console application.
        Type[] inputModelTypes = GetConsoleApplicationInputModelTypes(consoleApplicationType);

        // If there aren't any input model types then there's structured input for this application.
        if (inputModelTypes.Length is 0)
        {
            // Localize our service provider.
            ServiceProvider provider = _services.BuildServiceProvider();

            // Execute the console application without any input model.
            await (
                provider.GetRequiredService(consoleApplicationType) as IConsoleApplication
            )!.ExecuteAsync(_arguments, stoppingToken);

            // We're done, return from the method.
            return;
        }

        // Parse the arguments from the command-line interface.
        await ParseAsync<IConsoleCommandInputModel>(
            (m, p, t) =>
                (p.GetRequiredService(consoleApplicationType) as IConsoleApplication)!.ExecuteAsync(
                    m,
                    t
                ),
            stoppingToken,
            inputModelTypes
        );
    }

    /// <summary>
    ///     This method asynchronously executes a <see cref="IConsoleApplication" />
    ///     after parsing the command-line arguments.
    /// </summary>
    /// <param name="stoppingToken">Optional cancellation token to stop the execution.</param>
    /// <typeparam name="TConsoleApplication">The type of the console application to execute.</typeparam>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the execution is complete.</returns>
    public Task ApplicationAsync<TConsoleApplication>(CancellationToken stoppingToken = default)
        where TConsoleApplication : class, IConsoleApplication =>
        ApplicationAsync(typeof(TConsoleApplication), stoppingToken);

    /// <summary>
    ///     This method uses a <paramref name="handler" /> to configure the runtime <see name="_services" />.
    /// </summary>
    /// <param name="handler">The handler to configure the services.</param>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand ConfigureServices(Action<IServiceCollection> handler)
    {
        // Invoke the handler to configure the services.
        handler?.Invoke(_services);

        // We're done, return the instance.
        return this;
    }

    /// <summary>
    ///     This method retries a registered service instance from the <see cref="_services" />.
    /// </summary>
    /// <typeparam name="TService">The type of the service to retrieve.</typeparam>
    /// <returns>The service instance of the specified type <typeparamref name="TService" />.</returns>
    public TService GetService<TService>()
        where TService : class => _services.BuildServiceProvider().GetRequiredService<TService>();

    /// <summary>
    ///     This method fluently sets the not parsed handler into the instance.
    /// </summary>
    /// <param name="handler">
    ///     The <see cref="Action" /> to execute when the command-line arguments are not parsed.
    /// </param>
    /// <returns>The updated <see cref="IConsoleCommand" /> instance.</returns>
    public IConsoleCommand WithNotParsed<TException>(
        IConsoleCommand.CommandNotParsedAsyncDelegate<TException> handler
    )
        where TException : Exception
    {
        // Ensure we have a valid handler to register before proceeding.
        if (handler is not null)
            // Wrap the typed handler in a delegate accepting the common Exception base type instead of
            // casting it directly - a direct "as" cast here is an unsound delegate-variance conversion
            // (narrowing from a specific exception type to the base Exception type) that silently
            // evaluates to null for any TException other than exactly Exception, which would either
            // drop the handler entirely or throw a NullReferenceException when it's later invoked.
            // The wrapper only forwards to the handler when the raised exception is actually
            // compatible with TException, otherwise it's a safe no-op.
            _notParsed[typeof(TException)] = exception =>
            {
                if (exception is TException typedException)
                    handler(typedException);
            };

        // We're done, return the instance.
        return this;
    }

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
        IConsoleCommand.CommandParsedAsyncDelegate<TInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel : class, IConsoleCommandInputModel =>
        ParseAsync(parsedDelegate, stoppingToken, typeof(TInputModel));

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel =>
        ParseAsync(parsedDelegate, stoppingToken, typeof(TInputModel1), typeof(TInputModel2));

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
        CancellationToken stoppingToken = default
    )
        where TInputModel1 : class, IConsoleCommandInputModel
        where TInputModel2 : class, IConsoleCommandInputModel
        where TInputModel3 : class, IConsoleCommandInputModel
        where TInputModel4 : class, IConsoleCommandInputModel
        where TInputModel5 : class, IConsoleCommandInputModel
        where TInputModel6 : class, IConsoleCommandInputModel
        where TInputModel7 : class, IConsoleCommandInputModel
        where TInputModel8 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel9 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel10 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel11 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel12 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel13 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel14 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel15 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel16 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel17 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel18 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel19 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel20 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel21 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel22 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel23 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22),
            typeof(TInputModel23)
        );

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
        IConsoleCommand.CommandParsedAsyncDelegate<IConsoleCommandInputModel> parsedDelegate,
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
        where TInputModel24 : class, IConsoleCommandInputModel =>
        ParseAsync(
            parsedDelegate,
            stoppingToken,
            typeof(TInputModel1),
            typeof(TInputModel2),
            typeof(TInputModel3),
            typeof(TInputModel4),
            typeof(TInputModel5),
            typeof(TInputModel6),
            typeof(TInputModel7),
            typeof(TInputModel8),
            typeof(TInputModel9),
            typeof(TInputModel10),
            typeof(TInputModel11),
            typeof(TInputModel12),
            typeof(TInputModel13),
            typeof(TInputModel14),
            typeof(TInputModel15),
            typeof(TInputModel16),
            typeof(TInputModel17),
            typeof(TInputModel18),
            typeof(TInputModel19),
            typeof(TInputModel20),
            typeof(TInputModel21),
            typeof(TInputModel22),
            typeof(TInputModel23),
            typeof(TInputModel24)
        );
}
