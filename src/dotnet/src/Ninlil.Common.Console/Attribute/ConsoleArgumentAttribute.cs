// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This abstract class is the common base of the attributes that declare an input model's
///     verb and arguments (<see cref="ConsoleVerbAttribute" />, <see cref="NamedConsoleArgumentAttribute" />
///     and <see cref="PositionalConsoleArgumentAttribute" />). The parser stores any of them as
///     the <c>Attribute</c> of a <see cref="Model.ParsedConsoleArgument" />.
/// </summary>
public abstract class ConsoleArgumentAttribute : System.Attribute;
