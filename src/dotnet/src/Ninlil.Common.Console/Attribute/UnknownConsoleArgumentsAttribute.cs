// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This attribute marks the input model property that receives every command-line token no
///     declared argument or nested verb claimed. The property must be a
///     <see cref="Model.ConsoleCommandArgumentCollection" />; the tokens keep their original order.
///     It lets a model accept pass-through arguments without declaring each one.
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
