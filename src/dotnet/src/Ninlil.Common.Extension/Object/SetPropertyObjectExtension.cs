using System.Linq.Expressions;
using System.Reflection;

// Define our namespace.
namespace Ninlil.Common.Extension.Object;

/// <summary>
///     This static class sets a public property on an object by name or by a property selector
///     expression, using reflection on the compile-time type <c>TObject</c>.
/// </summary>
public static class SetPropertyObjectExtension
{
    /// <summary>
    ///     This extension method sets a property on an object
    ///     <paramref name="instance"/> by its <paramref name="propertyName" />.
    /// </summary>
    /// <param name="instance">The object instance to set the property on.</param>
    /// <param name="propertyName">The name of the property to set.</param>
    /// <param name="value">The value to set the property to.</param>
    /// <typeparam name="TObject">The type of the object <paramref name="instance" />.</typeparam>
    /// <returns>The updated object <paramref name="instance" />.</returns>
    /// <exception cref="ArgumentException">When the <paramref name="propertyName" /> is not writable.</exception>
    /// <exception cref="ArgumentNullException">
    ///     When the <paramref name="propertyName" /> is null or doesn't exist on
    ///     <typeparamref name="TObject" />. An empty name throws <see cref="ArgumentException" />.
    /// </exception>
    public static TObject SetProperty<TObject>(
        this TObject instance,
        string propertyName,
        object value
    )
        where TObject : class
    {
        // Ensure we have a property name to set.
        ArgumentException.ThrowIfNullOrEmpty(propertyName?.Trim(), nameof(propertyName));

        // Trim the property name to remove any leading or trailing whitespace.
        propertyName = propertyName.Trim();

        // Localize the property info.
        PropertyInfo property = typeof(TObject).GetProperty(propertyName);

        // Ensure the property exists.
        ArgumentNullException.ThrowIfNull(property);

        // Ensure the property is writable.
        if (!property.CanWrite)
            throw new ArgumentException(
                $"Property '{propertyName}' is not writable.",
                nameof(propertyName)
            );

        // Reset the value on the property.
        property.SetValue(instance, value);

        // We're done, return the instance.
        return instance;
    }

    /// <summary>
    ///     This extension method sets a property on an object
    ///     <paramref name="instance"/> by the <paramref name="propertySelector" />.
    /// </summary>
    /// <param name="instance">The object instance to set the property on.</param>
    /// <param name="propertySelector">The expression to select the property to set.</param>
    /// <param name="value">The value to set the property to.</param>
    /// <typeparam name="TObject">The type of the object <paramref name="instance" />.</typeparam>
    /// <typeparam name="TValue">The type of the <paramref name="value"/> to set the property to.</typeparam>
    /// <returns>The updated object <paramref name="instance" />.</returns>
    public static TObject SetProperty<TObject, TValue>(
        this TObject instance,
        Expression<Func<TObject, TValue>> propertySelector,
        TValue value
    )
        where TObject : class =>
        SetProperty(
            instance,
            ((propertySelector?.Body as MemberExpression)?.Member as PropertyInfo)?.Name,
            value
        );
}
