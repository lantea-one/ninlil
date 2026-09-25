// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This static class produces the runtime equivalent of <c>default(T)</c> for a
///     <see cref="System.Type" />: a zeroed value type, or <see langword="null" /> for reference and
///     nullable types. The console parser uses it to tell whether a property still holds its default.
/// </summary>
// ReSharper disable once UnusedType.Global
public static class DefaultValueTypeExtension
{
    /// <summary>
    ///     This method generates the default value for the <see cref="System.Type" /> <paramref name="instance" />.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to generate the default value for.</param>
    /// <returns>The default value for the <see cref="System.Type" /> <paramref name="instance" />.</returns>
    // ReSharper disable once MemberCanBePrivate.Global
    public static dynamic GetDefaultValue(this System.Type instance) =>
        Nullable.GetUnderlyingType(instance) is not null ? null
        : instance.IsValueType ? Activator.CreateInstance(instance)
        : null;

    /// <summary>
    ///     This method generates the default value for the <see cref="System.Type" /> <paramref name="instance" />.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type" /> to generate the default value for.</param>
    /// <typeparam name="TValue">The expected type of the default value.</typeparam>
    /// <returns>
    /// The <typeparamref name="TValue" /> typed default value for
    /// the <see cref="System.Type" /> <paramref name="instance" />.
    /// </returns>
    // ReSharper disable once UnusedMember.Global
    public static TValue GetDefaultValue<TValue>(this System.Type instance) =>
        (TValue)instance.GetDefaultValue();
}
