using Ninlil.Common.Extension.Object;

// Define our namespace.
namespace Ninlil.Common.Extension.List;

/// <summary>
///     This static class adds console-friendly formatting to <see cref="List{T}" />.
/// </summary>
public static class ListExtension
{
    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string for console output using
    ///     <see cref="ToStringValueObjectExtension.ToStringValue" />, or <c>"null"</c> when it is null.
    /// </summary>
    /// <param name="instance">The instance of the <see cref="List{TValue}" /> to convert.</param>
    /// <typeparam name="TValue">The expected type of value in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TValue>(this List<TValue> instance) =>
        instance?.ToStringValue() ?? "null";
}
