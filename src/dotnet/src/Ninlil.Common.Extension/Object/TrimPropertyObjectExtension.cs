using System.Linq.Expressions;
using System.Reflection;

// Define our namespace.
namespace Ninlil.Common.Extension.Object;

/// <summary>
///     This static class trims a public string property on an object in place, by name or by a
///     property selector expression.
/// </summary>
public static class TrimPropertyObjectExtension
{
    /// <summary>
    ///     This extension method trims a property on an object
    ///     <paramref name="instance"/> by its <paramref name="propertyName" />.
    /// </summary>
    /// <param name="instance">The object instance to trim the property on.</param>
    /// <param name="propertyName">The name of the property to trim.</param>
    /// <param name="defaultValue">
    ///     The value to use when the property is null. A blank value is trimmed to an empty string,
    ///     not replaced.
    /// </param>
    /// <typeparam name="TObject">The type of the object <paramref name="instance" />.</typeparam>
    /// <returns>The updated object <paramref name="instance" />.</returns>
    /// <exception cref="ArgumentException">
    ///     When the property is not writable or is not of type <see cref="string" />.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     When the <paramref name="propertyName" /> is null or doesn't exist on
    ///     <typeparamref name="TObject" />.
    /// </exception>
    public static TObject TrimProperty<TObject>(
        this TObject instance,
        string propertyName,
        string defaultValue = null
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

        // Check the type of the property for a string.
        if (property.PropertyType != typeof(string))
            throw new ArgumentException(
                $"Property '{propertyName}' is not of type string.",
                nameof(propertyName)
            );

        // We're done, reset the value on the property then return the updated instance.
        return instance.SetProperty(
            property.Name,
            property.GetValue(instance)?.ToString()?.Trim() ?? defaultValue
        );
    }

    /// <summary>
    ///     This extension method trims a property on an object
    ///     <paramref name="instance"/> by its <paramref name="propertySelector" />.
    /// </summary>
    /// <param name="instance">The object instance to trim the property on.</param>
    /// <param name="propertySelector">The expression to select the property to trim.</param>
    /// <param name="defaultValue">The value to use when the property is null.</param>
    /// <typeparam name="TObject">The type of the object <paramref name="instance" />.</typeparam>
    /// <returns>The updated object <paramref name="instance" />.</returns>
    public static TObject TrimProperty<TObject>(
        this TObject instance,
        Expression<Func<TObject, string>> propertySelector,
        string defaultValue = null
    )
        where TObject : class =>
        TrimProperty(
            instance,
            ((propertySelector?.Body as MemberExpression)?.Member as PropertyInfo)?.Name,
            defaultValue
        );
}
