// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This attribute is used to mark a field or property in a console application that should be populated with any
///     unknown console arguments that are not explicitly defined as parameters. When applied, the marked field or
///     property will receive a collection of the remaining console arguments, allowing for flexible handling of
///     additional input without requiring predefined parameters for each argument. This is particularly useful for
///     scenarios where the number or type of console arguments may vary and cannot be predetermined.
/// </summary>
/// <example>
///     <code>
///         public class MyConsoleAppInputModel
///         {
///             [UnknownConsoleArguments]
///             public ConsoleCommandArgumentCollection RemainingArguments { get; set; }
///         }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class UnknownConsoleArgumentsAttribute : System.Attribute;
