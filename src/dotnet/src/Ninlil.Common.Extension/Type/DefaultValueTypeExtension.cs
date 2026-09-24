// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This clas maintains the <see cref="System.Type" /> object extensions for generating default values.
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
