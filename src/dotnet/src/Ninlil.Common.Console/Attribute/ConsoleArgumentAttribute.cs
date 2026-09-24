// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This class provides a common ancestry for all console argument attributes, which are used to annotate
///     properties in a class that represents a console command. By inheriting from this base class, all console
///     argument attributes can be easily identified and processed when parsing command-line input, allowing for
///     a consistent and extensible way to define command-line arguments in a .NET application.
/// </summary>
public abstract class ConsoleArgumentAttribute : System.Attribute;
