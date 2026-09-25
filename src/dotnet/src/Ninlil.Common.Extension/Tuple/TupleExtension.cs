using Ninlil.Common.Extension.Object;

// Define our namespace.
namespace Ninlil.Common.Extension.Tuple;

/// <summary>
///     This static class adds console-friendly formatting to <see cref="System.Tuple" /> types of
///     every arity. Each overload formats with
///     <see cref="ToStringValueObjectExtension.ToStringValue" />, giving <c>(a,b,...)</c>, or
///     <c>"null"</c> for a null tuple.
/// </summary>
public static class TupleExtension
{
    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string representation suitable for console output.
    /// </summary>
    /// <param name="instance">The instance of the <see cref="Tuple{TItem1,TItem2}" /> to convert.</param>
    /// <typeparam name="TItem1">The expected type of the first item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem2">The expected type of the second item in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TItem1, TItem2>(this Tuple<TItem1, TItem2> instance) =>
        instance?.ToStringValue() ?? "null";

    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string representation suitable for console output.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="Tuple{TItem1,TItem2,TItem3}" /> to convert.
    /// </param>
    /// <typeparam name="TItem1">The expected type of the first item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem2">The expected type of the second item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem3">The expected type of the third item in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TItem1, TItem2, TItem3>(
        this Tuple<TItem1, TItem2, TItem3> instance
    ) => instance?.ToStringValue() ?? "null";

    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string representation suitable for console output.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="Tuple{TItem1,TItem2,TItem3,TItem4}" /> to convert.
    /// </param>
    /// <typeparam name="TItem1">The expected type of the first item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem2">The expected type of the second item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem3">The expected type of the third item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem4">The expected type of the fourth item in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TItem1, TItem2, TItem3, TItem4>(
        this Tuple<TItem1, TItem2, TItem3, TItem4> instance
    ) => instance?.ToStringValue() ?? "null";

    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string representation suitable for console output.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="Tuple{TItem1,TItem2,TItem3,TItem4,TItem5}" /> to convert.
    /// </param>
    /// <typeparam name="TItem1">The expected type of the first item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem2">The expected type of the second item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem3">The expected type of the third item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem4">The expected type of the fourth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem5">The expected type of the fifth item in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TItem1, TItem2, TItem3, TItem4, TItem5>(
        this Tuple<TItem1, TItem2, TItem3, TItem4, TItem5> instance
    ) => instance?.ToStringValue() ?? "null";

    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string representation suitable for console output.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="Tuple{TItem1,TItem2,TItem3,TItem4,TItem5,TItem6}" /> to convert.
    /// </param>
    /// <typeparam name="TItem1">The expected type of the first item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem2">The expected type of the second item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem3">The expected type of the third item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem4">The expected type of the fourth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem5">The expected type of the fifth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem6">The expected type of the sixth item in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TItem1, TItem2, TItem3, TItem4, TItem5, TItem6>(
        this Tuple<TItem1, TItem2, TItem3, TItem4, TItem5, TItem6> instance
    ) => instance?.ToStringValue() ?? "null";

    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string representation suitable for console output.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="Tuple{TItem1,TItem2,TItem3,TItem4,TItem5,TItem6,TItem7}" /> to convert.
    /// </param>
    /// <typeparam name="TItem1">The expected type of the first item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem2">The expected type of the second item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem3">The expected type of the third item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem4">The expected type of the fourth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem5">The expected type of the fifth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem6">The expected type of the sixth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem7">The expected type of the seventh item in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TItem1, TItem2, TItem3, TItem4, TItem5, TItem6, TItem7>(
        this Tuple<TItem1, TItem2, TItem3, TItem4, TItem5, TItem6, TItem7> instance
    ) => instance?.ToStringValue() ?? "null";

    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string representation suitable for console output.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="Tuple{TItem1,TItem2,TItem3,TItem4,TItem5,TItem6,TItem7,TRest}" /> to convert.
    /// </param>
    /// <typeparam name="TItem1">The expected type of the first item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem2">The expected type of the second item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem3">The expected type of the third item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem4">The expected type of the fourth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem5">The expected type of the fifth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem6">The expected type of the sixth item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TItem7">The expected type of the seventh item in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TRest">The expected type of the rest of the items in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<
        TItem1,
        TItem2,
        TItem3,
        TItem4,
        TItem5,
        TItem6,
        TItem7,
        TRest
    >(this Tuple<TItem1, TItem2, TItem3, TItem4, TItem5, TItem6, TItem7, TRest> instance) =>
        instance?.ToStringValue() ?? "null";
}
